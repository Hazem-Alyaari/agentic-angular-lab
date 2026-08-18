using System.Runtime.CompilerServices;
using System.Text;
using AGUI.Abstractions;
using Agent.Api.Llm;
using Agent.Api.Tools;
using Microsoft.Extensions.Options;

namespace Agent.Api.Agents;

/// <summary>
/// Owns the AG-UI run lifecycle, including the explicit tool loop:
/// LLM → tool call → ToolRegistry.Execute → tool result → LLM again → final text.
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
        "You are a helpful HR assistant. Use get_employee or get_leave_balance only when the user asks about a specific employee's department, id, or leave balance. Answer general questions directly without tools.";

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

        yield return new RunStartedEvent
        {
            ThreadId = threadId,
            RunId = runId
        };

        var conversation = MapMessages(input.Messages);
        EnsureSystemPrompt(conversation);
        var tools = toolRegistry.GetDefinitions();

        for (var iteration = 1; iteration <= MaxToolIterations; iteration++)
        {
            logger.LogInformation("LLM iteration {Iteration} started", iteration);

            var turn = new LlmTurn();
            await foreach (var agUiEvent in StreamTurn(conversation, tools, turn, cancellationToken))
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
                    RunId = runId
                };
                yield break;
            }

            conversation.Add(new LlmMessage(
                "assistant",
                string.IsNullOrWhiteSpace(turn.AssistantText) ? null : turn.AssistantText,
                turn.ToolCalls));

            foreach (var call in turn.ToolCalls)
            {
                logger.LogInformation("Tool requested: {ToolName}", call.Name);

                string resultJson;
                try
                {
                    resultJson = await ExecuteToolAsync(call, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    resultJson = ToolJson.Error("The tool could not be executed.");
                }

                logger.LogInformation("Tool executed: {ToolName}", call.Name);

                yield return new ToolCallResultEvent
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    ToolCallId = call.Id,
                    Content = resultJson,
                    Role = "tool"
                };

                conversation.Add(new LlmMessage("tool", resultJson, ToolCallId: call.Id));
            }
        }

        yield return new RunErrorEvent
        {
            Message = "The agent stopped after too many tool iterations.",
            Code = "max_tool_iterations"
        };
    }

    private async Task<string> ExecuteToolAsync(LlmToolCall call, CancellationToken cancellationToken)
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

                case AGUIAssistantMessage assistant when !string.IsNullOrWhiteSpace(assistant.Content):
                    result.Add(new LlmMessage("assistant", assistant.Content));
                    break;
            }
        }

        return result;
    }

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

    private sealed class LlmTurn
    {
        public List<LlmToolCall> ToolCalls { get; } = [];

        public string? AssistantText { get; set; }

        public string? Error { get; set; }
    }
}
