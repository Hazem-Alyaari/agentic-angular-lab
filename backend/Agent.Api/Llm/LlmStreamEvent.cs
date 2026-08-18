namespace Agent.Api.Llm;

/// <summary>
/// Provider-neutral stream items. Not AG-UI events and not OpenAI SDK types.
/// AgentRunService is the only layer that maps these to AG-UI.
/// </summary>
public abstract record LlmStreamEvent;

public sealed record LlmTextDelta(string Text) : LlmStreamEvent;

public sealed record LlmToolCallStarted(string ToolCallId, string ToolName) : LlmStreamEvent;

public sealed record LlmToolCallArgumentsDelta(string ToolCallId, string ArgumentsDelta) : LlmStreamEvent;

public sealed record LlmToolCallCompleted(string ToolCallId, string ToolName, string Arguments) : LlmStreamEvent;
