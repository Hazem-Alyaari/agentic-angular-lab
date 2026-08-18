namespace Agent.Api.Llm;

public interface ILlmProvider
{
    IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        CancellationToken cancellationToken = default);
}
