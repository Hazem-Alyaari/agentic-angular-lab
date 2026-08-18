using System.Text.Json;

namespace Agent.Api.Llm;

public sealed record LlmToolDefinition(
    string Name,
    string Description,
    JsonElement Parameters);
