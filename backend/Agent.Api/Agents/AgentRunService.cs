using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using Agent.Api.Llm;
using Agent.Api.Tools;
using Microsoft.Extensions.Options;

namespace Agent.Api.Agents;

/// <summary>
/// Owns the AG-UI run lifecycle, including the explicit tool loop:
/// LLM → tool call → ToolRegistry.Execute (server) or interrupt (frontend)
/// → tool result → LLM again → final text.
/// Does not reference OpenAI SDK types.
/// </summary>
public sealed class AgentRunService(
    ILlmProvider llmProvider,
    ToolRegistry toolRegistry,
    IOptions<LlmOptions> llmOptions,
    ILogger<AgentRunService> logger)
{
    private int MaxToolIterations => Math.Max(1, llmOptions.Value.MaxToolIterations);

    private const string SystemPrompt =
        "You are a helpful HR assistant. " +
        "Server tools (executed by the backend): get_employee, get_leave_balance. " +
        "Frontend tools (executed by the Angular app): navigate_to_employee — use this to open an employee profile. " +
        "navigate_to_employee requires employeeId as an integer. If you only have a name, call get_employee first. " +
        "Answer general questions directly without tools.";

    public static bool TryValidate(RunAgentInput input, out string error)
    {
        if (MapMessages(input.Messages).Count == 0)
        {
            error = "RunAgentInput.messages must include at least one system, user, or assistant text message.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public async IAsyncEnumerable<BaseEvent> RunAsync(
        RunAgentInput input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var threadId = string.IsNullOrWhiteSpace(input.ThreadId)
            ? Guid.NewGuid().ToString("N")
            : input.ThreadId;

        var runId = string.IsNullOrWhiteSpace(input.RunId)
            ? Guid.NewGuid().ToString("N")
            : input.RunId;

        logger.LogInformation("Agent run started {RunId}", runId);

        var started = new RunStartedEvent
        {
            ThreadId = threadId,
            RunId = runId
        };
        if (!string.IsNullOrWhiteSpace(input.ParentRunId))
        {
            started.ParentRunId = input.ParentRunId;
        }

        yield return started;

        var conversation = MapMessages(input.Messages);
        EnsureSystemPrompt(conversation);
        var catalog = ToolCatalog.Merge(toolRegistry.GetDefinitions(), input.Tools, logger);

        await foreach (var resumeEvent in ApplyResumeAsync(input.Resume, conversation, cancellationToken))
        {
            yield return resumeEvent;
        }

        var pending = UnansweredToolCalls(conversation).ToList();
        if (pending.Count > 0)
        {
            yield return new RunErrorEvent
            {
                Message = "The run is missing tool results required to continue.",
                Code = "missing_tool_result"
            };
            yield break;
        }

        for (var iteration = 1; iteration <= MaxToolIterations; iteration++)
        {
            logger.LogInformation("LLM iteration {Iteration} started", iteration);

            var turn = new LlmTurn();
            await foreach (var agUiEvent in StreamTurn(conversation, catalog.Definitions, turn, cancellationToken))
            {
                yield return agUiEvent;
            }

            if (turn.Error is not null)
            {
                yield return new RunErrorEvent
                {
                    Message = turn.Error,
                    Code = "llm_error"
                };
                yield break;
            }

            if (turn.ToolCalls.Count == 0)
            {
                logger.LogInformation("Agent run completed {RunId}", runId);
                yield return new RunFinishedEvent
                {
                    ThreadId = threadId,
                    RunId = runId,
                    Outcome = new RunFinishedSuccessOutcome()
                };
                yield break;
            }

            conversation.Add(new LlmMessage(
                "assistant",
                string.IsNullOrWhiteSpace(turn.AssistantText) ? null : turn.AssistantText,
                turn.ToolCalls));

            var frontendCalls = new List<LlmToolCall>();
            var serverCalls = new List<LlmToolCall>();
            foreach (var call in turn.ToolCalls)
            {
                if (catalog.IsFrontend(call.Name))
                {
                    frontendCalls.Add(call);
                }
                else
                {
                    serverCalls.Add(call);
                }
            }

            foreach (var call in serverCalls)
            {
                logger.LogInformation("Server tool requested: {ToolName}", call.Name);

                string resultJson;
                try
                {
                    resultJson = await ExecuteServerToolAsync(call, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    resultJson = ToolJson.Error("The tool could not be executed.");
                }

                logger.LogInformation("Server tool executed: {ToolName}", call.Name);

                yield return CreateToolResultEvent(call.Id, resultJson);
                conversation.Add(new LlmMessage("tool", resultJson, ToolCallId: call.Id));
            }

            if (frontendCalls.Count == 0)
            {
                continue;
            }

            foreach (var call in frontendCalls)
            {
                logger.LogInformation(
                    "Frontend tool requested: {ToolName} — not executing on server",
                    call.Name);
            }

            yield return new MessagesSnapshotEvent
            {
                Messages = ToAgUiMessages(conversation)
            };

            yield return new RunFinishedEvent
            {
                ThreadId = threadId,
                RunId = runId,
                Outcome = new RunFinishedInterruptOutcome
                {
                    Interrupts = frontendCalls.Select(call => new AGUIInterrupt
                    {
                        Id = call.Id,
                        Reason = InterruptReasons.ToolCall,
                        ToolCallId = call.Id,
                        Message = $"Frontend tool '{call.Name}' is waiting for the Angular app to execute."
                    }).ToList()
                }
            };
            yield break;
        }

        yield return new RunErrorEvent
        {
            Message = "The agent stopped after too many tool iterations.",
            Code = "max_tool_iterations"
        };
    }

    private static async IAsyncEnumerable<BaseEvent> ApplyResumeAsync(
        IList<AGUIResume>? resumes,
        List<LlmMessage> conversation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (resumes is null || resumes.Count == 0)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resume in resumes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(resume.InterruptId) || !seen.Add(resume.InterruptId))
            {
                continue;
            }

            var toolCallId = resume.InterruptId;
            string content;
            if (string.Equals(resume.Status, ResumeStatus.Cancelled, StringComparison.Ordinal))
            {
                content = ToolJson.Error("Frontend tool execution was cancelled.");
            }
            else
            {
                content = FindToolContent(conversation, toolCallId)
                    ?? PayloadToJson(resume.Payload)
                    ?? ToolJson.Error("Frontend tool result was missing.");
            }

            if (!conversation.Any(message =>
                    message.Role == "tool"
                    && string.Equals(message.ToolCallId, toolCallId, StringComparison.Ordinal)))
            {
                conversation.Add(new LlmMessage("tool", content, ToolCallId: toolCallId));
            }

            yield return CreateToolResultEvent(toolCallId, content);
        }
    }

    private async Task<string> ExecuteServerToolAsync(LlmToolCall call, CancellationToken cancellationToken)
    {
        var tool = toolRegistry.Get(call.Name);
        if (tool is null)
        {
            return ToolJson.Error($"Unknown tool '{call.Name}'.");
        }

        return await tool.ExecuteAsync(call.Arguments, cancellationToken);
    }

    private async IAsyncEnumerable<BaseEvent> StreamTurn(
        IReadOnlyList<LlmMessage> conversation,
        IReadOnlyList<LlmToolDefinition> tools,
        LlmTurn turn,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerator<LlmStreamEvent>? enumerator = null;
        var textOpen = false;
        string? textMessageId = null;
        var startedToolIds = new HashSet<string>(StringComparer.Ordinal);
        var assistantText = new StringBuilder();

        try
        {
            enumerator = llmProvider
                .StreamAsync(conversation, tools, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                bool moved;
                string? readError = null;

                try
                {
                    moved = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogWarning(ex, "LLM stream failed");
                    readError = ex.Message;
                    moved = false;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "LLM stream failed");
                    readError = "The language model request failed.";
                    moved = false;
                }

                if (readError is not null)
                {
                    if (textOpen && textMessageId is not null)
                    {
                        yield return new TextMessageEndEvent { MessageId = textMessageId };
                    }

                    turn.Error = readError;
                    yield break;
                }

                if (!moved)
                {
                    break;
                }

                switch (enumerator.Current)
                {
                    case LlmTextDelta text:
                        if (!textOpen)
                        {
                            textMessageId = Guid.NewGuid().ToString("N");
                            textOpen = true;
                            yield return new TextMessageStartEvent
                            {
                                MessageId = textMessageId,
                                Role = "assistant"
                            };
                        }

                        assistantText.Append(text.Text);
                        yield return new TextMessageContentEvent
                        {
                            MessageId = textMessageId!,
                            Delta = text.Text
                        };
                        break;

                    case LlmToolCallStarted started:
                        if (textOpen && textMessageId is not null)
                        {
                            yield return new TextMessageEndEvent { MessageId = textMessageId };
                            textOpen = false;
                        }

                        startedToolIds.Add(started.ToolCallId);
                        yield return new ToolCallStartEvent
                        {
                            ToolCallId = started.ToolCallId,
                            ToolCallName = started.ToolName,
                            ParentMessageId = textMessageId
                        };
                        break;

                    case LlmToolCallArgumentsDelta args:
                        yield return new ToolCallArgsEvent
                        {
                            ToolCallId = args.ToolCallId,
                            Delta = args.ArgumentsDelta
                        };
                        break;

                    case LlmToolCallCompleted completed:
                        if (!startedToolIds.Contains(completed.ToolCallId))
                        {
                            yield return new ToolCallStartEvent
                            {
                                ToolCallId = completed.ToolCallId,
                                ToolCallName = completed.ToolName,
                                ParentMessageId = textMessageId
                            };
                        }

                        yield return new ToolCallEndEvent
                        {
                            ToolCallId = completed.ToolCallId
                        };

                        turn.ToolCalls.Add(new LlmToolCall(
                            completed.ToolCallId,
                            completed.ToolName,
                            completed.Arguments));
                        break;
                }
            }
        }
        finally
        {
            if (enumerator is not null)
            {
                await enumerator.DisposeAsync();
            }
        }

        if (textOpen && textMessageId is not null)
        {
            yield return new TextMessageEndEvent { MessageId = textMessageId };
        }

        turn.AssistantText = assistantText.ToString();
    }

    internal static List<LlmMessage> MapMessages(IList<AGUIMessage>? messages)
    {
        var result = new List<LlmMessage>();
        if (messages is null)
        {
            return result;
        }

        foreach (var message in messages)
        {
            switch (message)
            {
                case AGUISystemMessage system when !string.IsNullOrWhiteSpace(system.Content):
                    result.Add(new LlmMessage("system", system.Content));
                    break;

                case AGUIUserMessage user:
                {
                    var text = ExtractUserText(user.Content);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        result.Add(new LlmMessage("user", text));
                    }

                    break;
                }

                case AGUIAssistantMessage assistant:
                {
                    var toolCalls = MapToolCalls(assistant.ToolCalls);
                    if (string.IsNullOrWhiteSpace(assistant.Content) && toolCalls.Count == 0)
                    {
                        break;
                    }

                    result.Add(new LlmMessage(
                        "assistant",
                        string.IsNullOrWhiteSpace(assistant.Content) ? null : assistant.Content,
                        toolCalls.Count == 0 ? null : toolCalls));
                    break;
                }

                case AGUIToolMessage tool when !string.IsNullOrWhiteSpace(tool.ToolCallId):
                    result.Add(new LlmMessage("tool", tool.Content ?? string.Empty, ToolCallId: tool.ToolCallId));
                    break;
            }
        }

        return result;
    }

    private static List<LlmToolCall> MapToolCalls(IList<AGUIToolCall>? toolCalls)
    {
        var result = new List<LlmToolCall>();
        if (toolCalls is null)
        {
            return result;
        }

        foreach (var call in toolCalls)
        {
            if (string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Function?.Name))
            {
                continue;
            }

            result.Add(new LlmToolCall(
                call.Id,
                call.Function.Name,
                string.IsNullOrWhiteSpace(call.Function.Arguments) ? "{}" : call.Function.Arguments));
        }

        return result;
    }

    private static List<AGUIMessage> ToAgUiMessages(IReadOnlyList<LlmMessage> conversation)
    {
        var result = new List<AGUIMessage>();
        foreach (var message in conversation)
        {
            switch (message.Role)
            {
                case "user" when !string.IsNullOrWhiteSpace(message.Content):
                    result.Add(new AGUIUserMessage
                    {
                        Id = NewId(),
                        Content = message.Content
                    });
                    break;

                case "assistant":
                    result.Add(new AGUIAssistantMessage
                    {
                        Id = NewId(),
                        Content = string.IsNullOrWhiteSpace(message.Content) ? string.Empty : message.Content,
                        ToolCalls = message.ToolCalls?.Select(call => new AGUIToolCall
                        {
                            Id = call.Id,
                            Type = "function",
                            Function = new AGUIToolCallFunction
                            {
                                Name = call.Name,
                                Arguments = call.Arguments
                            }
                        }).ToList()
                    });
                    break;

                case "tool" when !string.IsNullOrWhiteSpace(message.ToolCallId):
                    result.Add(new AGUIToolMessage
                    {
                        Id = NewId(),
                        ToolCallId = message.ToolCallId,
                        Content = message.Content ?? string.Empty
                    });
                    break;
            }
        }

        return result;
    }

    private static IEnumerable<LlmToolCall> UnansweredToolCalls(IReadOnlyList<LlmMessage> conversation)
    {
        var answered = conversation
            .Where(message => message.Role == "tool" && !string.IsNullOrWhiteSpace(message.ToolCallId))
            .Select(message => message.ToolCallId!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var message in conversation)
        {
            if (message.ToolCalls is null)
            {
                continue;
            }

            foreach (var call in message.ToolCalls)
            {
                if (!answered.Contains(call.Id))
                {
                    yield return call;
                }
            }
        }
    }

    private static string? FindToolContent(IReadOnlyList<LlmMessage> conversation, string toolCallId) =>
        conversation.LastOrDefault(message =>
                message.Role == "tool"
                && string.Equals(message.ToolCallId, toolCallId, StringComparison.Ordinal))
            ?.Content;

    private static string? PayloadToJson(JsonElement? payload)
    {
        if (payload is not { } element)
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => element.GetRawText()
        };
    }

    private static ToolCallResultEvent CreateToolResultEvent(string toolCallId, string content) =>
        new()
        {
            MessageId = NewId(),
            ToolCallId = toolCallId,
            Content = content,
            Role = "tool"
        };

    private static void EnsureSystemPrompt(List<LlmMessage> conversation)
    {
        if (conversation.Any(message => message.Role == "system"))
        {
            return;
        }

        conversation.Insert(0, new LlmMessage("system", SystemPrompt));
    }

    private static string? ExtractUserText(AGUIUserContent content)
    {
        if (content.IsText && content.Value is string text)
        {
            return text;
        }

        if (content.Value is IList<AGUIInputContent> parts)
        {
            var chunks = parts
                .OfType<AGUITextInputContent>()
                .Select(part => part.Text)
                .Where(part => !string.IsNullOrWhiteSpace(part));

            var joined = string.Join("\n", chunks);
            return string.IsNullOrWhiteSpace(joined) ? null : joined;
        }

        return null;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private sealed class LlmTurn
    {
        public List<LlmToolCall> ToolCalls { get; } = [];

        public string? AssistantText { get; set; }

        public string? Error { get; set; }
    }
}
