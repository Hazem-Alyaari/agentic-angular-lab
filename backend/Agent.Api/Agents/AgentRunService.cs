using System.Runtime.CompilerServices;
using AGUI.Abstractions;
using Agent.Api.Llm;

namespace Agent.Api.Agents;

/// <summary>
/// Owns the AG-UI run lifecycle. Converts LLM text chunks into protocol events.
/// Does not know about HTTP/SSE transport or OpenAI SDK types.
/// </summary>
public sealed class AgentRunService(ILlmProvider llmProvider)
{
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

        var llmMessages = MapMessages(input.Messages);
        var messageId = Guid.NewGuid().ToString("N");

        yield return new RunStartedEvent
        {
            ThreadId = threadId,
            RunId = runId
        };

        yield return new TextMessageStartEvent
        {
            MessageId = messageId,
            Role = "assistant"
        };

        await foreach (var item in ReadModelChunks(llmMessages, cancellationToken))
        {
            if (item.Error is not null)
            {
                yield return new TextMessageEndEvent { MessageId = messageId };
                yield return new RunErrorEvent
                {
                    Message = item.Error,
                    Code = "llm_error"
                };
                yield break;
            }

            yield return new TextMessageContentEvent
            {
                MessageId = messageId,
                Delta = item.Chunk!
            };
        }

        yield return new TextMessageEndEvent
        {
            MessageId = messageId
        };

        yield return new RunFinishedEvent
        {
            ThreadId = threadId,
            RunId = runId
        };
    }

    private async IAsyncEnumerable<(string? Chunk, string? Error)> ReadModelChunks(
        IReadOnlyList<LlmMessage> llmMessages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerator<string>? enumerator = null;

        try
        {
            enumerator = llmProvider
                .StreamAsync(llmMessages, cancellationToken)
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
                    readError = ex.Message;
                    moved = false;
                }
                catch (Exception)
                {
                    readError = "The language model request failed.";
                    moved = false;
                }

                if (readError is not null)
                {
                    yield return (null, readError);
                    yield break;
                }

                if (!moved)
                {
                    yield break;
                }

                if (!string.IsNullOrEmpty(enumerator.Current))
                {
                    yield return (enumerator.Current, null);
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
                    result.Add(new LlmMessage("assistant", assistant.Content!));
                    break;
            }
        }

        return result;
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
}
