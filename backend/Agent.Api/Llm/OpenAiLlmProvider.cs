using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Agent.Api.Llm;

/// <summary>
/// Maps OpenAI streaming chat + tool calls into provider-neutral LlmStreamEvent values.
/// Does not emit AG-UI events and does not execute tools.
/// </summary>
public sealed class OpenAiLlmProvider(IOptions<LlmOptions> options) : ILlmProvider
{
    private readonly Lazy<ChatClient> _chatClient = new(() => CreateClient(options.Value));

    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatMessages = messages.Select(ToChatMessage).ToList();
        var completionOptions = new ChatCompletionOptions();
        foreach (var tool in tools)
        {
            completionOptions.Tools.Add(ChatTool.CreateFunctionTool(
                functionName: tool.Name,
                functionDescription: tool.Description,
                functionParameters: BinaryData.FromString(tool.Parameters.GetRawText())));
        }

        var pending = new Dictionary<int, PendingToolCall>();
        var client = _chatClient.Value;

        await foreach (var update in client
            .CompleteChatStreamingAsync(chatMessages, completionOptions, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    yield return new LlmTextDelta(part.Text);
                }
            }

            foreach (var toolUpdate in update.ToolCallUpdates)
            {
                if (!pending.TryGetValue(toolUpdate.Index, out var acc))
                {
                    acc = new PendingToolCall();
                    pending[toolUpdate.Index] = acc;
                }

                if (!string.IsNullOrEmpty(toolUpdate.ToolCallId))
                {
                    acc.Id = toolUpdate.ToolCallId;
                }

                if (!string.IsNullOrEmpty(toolUpdate.FunctionName) && acc.Name is null)
                {
                    acc.Name = toolUpdate.FunctionName;
                    if (!string.IsNullOrEmpty(acc.Id))
                    {
                        yield return new LlmToolCallStarted(acc.Id, acc.Name);
                        acc.StartedEmitted = true;
                    }
                }

                var argsDelta = toolUpdate.FunctionArgumentsUpdate?.ToString();
                if (!string.IsNullOrEmpty(argsDelta) && !string.IsNullOrEmpty(acc.Id))
                {
                    if (!acc.StartedEmitted && acc.Name is not null)
                    {
                        yield return new LlmToolCallStarted(acc.Id, acc.Name);
                        acc.StartedEmitted = true;
                    }

                    acc.Arguments.Append(argsDelta);
                    yield return new LlmToolCallArgumentsDelta(acc.Id, argsDelta);
                }
            }
        }

        foreach (var acc in pending.Values.OrderBy(item => item.Id))
        {
            if (string.IsNullOrEmpty(acc.Id) || string.IsNullOrEmpty(acc.Name))
            {
                continue;
            }

            if (!acc.StartedEmitted)
            {
                yield return new LlmToolCallStarted(acc.Id, acc.Name);
            }

            yield return new LlmToolCallCompleted(acc.Id, acc.Name, acc.Arguments.ToString());
        }
    }

    private static ChatClient CreateClient(LlmOptions llm)
    {
        if (string.IsNullOrWhiteSpace(llm.ApiKey))
        {
            throw new InvalidOperationException(
                "LLM API key is not configured. Set Llm:ApiKey via user-secrets or LLM_API_KEY.");
        }

        if (string.IsNullOrWhiteSpace(llm.Model))
        {
            throw new InvalidOperationException("Llm:Model is required.");
        }

        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(llm.BaseUrl))
        {
            clientOptions.Endpoint = new Uri(llm.BaseUrl);
        }

        if (llm.TimeoutSeconds > 0)
        {
            clientOptions.NetworkTimeout = TimeSpan.FromSeconds(llm.TimeoutSeconds);
        }

        return new ChatClient(
            model: llm.Model,
            credential: new ApiKeyCredential(llm.ApiKey),
            options: clientOptions);
    }

    private static ChatMessage ToChatMessage(LlmMessage message) =>
        message.Role switch
        {
            "system" => new SystemChatMessage(message.Content ?? string.Empty),
            "user" => new UserChatMessage(message.Content ?? string.Empty),
            "assistant" => ToAssistantMessage(message),
            "tool" => new ToolChatMessage(
                message.ToolCallId ?? throw new ArgumentException("Tool messages require ToolCallId."),
                message.Content ?? string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(message), $"Unsupported LLM role '{message.Role}'.")
        };

    private static AssistantChatMessage ToAssistantMessage(LlmMessage message)
    {
        if (message.ToolCalls is { Count: > 0 })
        {
            var calls = message.ToolCalls.Select(call =>
                ChatToolCall.CreateFunctionToolCall(
                    call.Id,
                    call.Name,
                    BinaryData.FromString(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments)));

            var assistant = new AssistantChatMessage(calls);
            if (!string.IsNullOrEmpty(message.Content))
            {
                assistant.Content.Add(ChatMessageContentPart.CreateTextPart(message.Content));
            }

            return assistant;
        }

        return new AssistantChatMessage(message.Content ?? string.Empty);
    }

    private sealed class PendingToolCall
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
        public bool StartedEmitted { get; set; }
    }
}
