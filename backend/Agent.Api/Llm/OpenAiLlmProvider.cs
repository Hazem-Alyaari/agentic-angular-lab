using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Agent.Api.Llm;

/// <summary>
/// OpenAI chat completions streaming via the official OpenAI .NET SDK.
/// OpenAI types stay inside this class; callers only see string chunks.
/// </summary>
public sealed class OpenAiLlmProvider(IOptions<LlmOptions> options) : ILlmProvider
{
    private readonly LlmOptions _options = options.Value;
    private readonly Lazy<ChatClient> _chatClient = new(() => CreateClient(options.Value));

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatMessages = messages.Select(ToChatMessage).ToList();
        var client = _chatClient.Value;

        await foreach (var update in client
            .CompleteChatStreamingAsync(chatMessages, cancellationToken: cancellationToken)
            .WithCancellation(cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    yield return part.Text;
                }
            }
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

        return new ChatClient(
            model: llm.Model,
            credential: new ApiKeyCredential(llm.ApiKey),
            options: clientOptions);
    }

    private static ChatMessage ToChatMessage(LlmMessage message) =>
        message.Role switch
        {
            "system" => new SystemChatMessage(message.Content),
            "assistant" => new AssistantChatMessage(message.Content),
            "user" => new UserChatMessage(message.Content),
            _ => throw new ArgumentOutOfRangeException(
                nameof(message),
                $"Unsupported LLM role '{message.Role}'.")
        };
}
