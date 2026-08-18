using System.Text.Json;
using Agent.Api.Llm;

namespace Agent.Api.Tools;

public interface IAgentTool
{
    string Name { get; }

    string Description { get; }

    JsonElement Parameters { get; }

    Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken);
}

public static class AgentToolExtensions
{
    public static LlmToolDefinition ToDefinition(this IAgentTool tool) =>
        new(tool.Name, tool.Description, tool.Parameters);
}
