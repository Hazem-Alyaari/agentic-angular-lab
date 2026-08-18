namespace Agent.Api.Llm;

public sealed record LlmToolCall(string Id, string Name, string Arguments);

public sealed record LlmMessage(
    string Role,
    string? Content = null,
    IReadOnlyList<LlmToolCall>? ToolCalls = null,
    string? ToolCallId = null);
