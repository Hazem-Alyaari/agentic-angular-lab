namespace Agent.Api.Llm;

public interface ILlmProvider
{
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken cancellationToken = default);
}
