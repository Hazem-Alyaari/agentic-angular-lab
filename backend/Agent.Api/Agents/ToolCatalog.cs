using System.Text.Json;
using AGUI.Abstractions;
using Agent.Api.Llm;

namespace Agent.Api.Agents;

/// <summary>
/// Merges backend-owned server tools with client-advertised frontend tools.
/// Server names always win so a client cannot hijack get_employee.
/// </summary>
public sealed class ToolCatalog
{
    private readonly Dictionary<string, CatalogedTool> _tools;

    private ToolCatalog(Dictionary<string, CatalogedTool> tools)
    {
        _tools = tools;
    }

    public IReadOnlyList<LlmToolDefinition> Definitions =>
        _tools.Values.Select(tool => tool.Definition).ToList();

    public static ToolCatalog Merge(
        IReadOnlyList<LlmToolDefinition> serverTools,
        IList<AGUITool>? frontendTools,
        ILogger logger)
    {
        var tools = new Dictionary<string, CatalogedTool>(StringComparer.Ordinal);

        foreach (var definition in serverTools)
        {
            tools[definition.Name] = new CatalogedTool(definition, ToolExecutionTarget.Server);
        }

        if (frontendTools is null)
        {
            return new ToolCatalog(tools);
        }

        foreach (var frontendTool in frontendTools)
        {
            if (string.IsNullOrWhiteSpace(frontendTool.Name))
            {
                continue;
            }

            if (tools.TryGetValue(frontendTool.Name, out var existing)
                && existing.Target == ToolExecutionTarget.Server)
            {
                logger.LogWarning(
                    "Ignoring client-advertised tool {ToolName} because a server tool already owns that name",
                    frontendTool.Name);
                continue;
            }

            tools[frontendTool.Name] = new CatalogedTool(
                new LlmToolDefinition(
                    frontendTool.Name,
                    frontendTool.Description ?? string.Empty,
                    CloneSchema(frontendTool.Parameters)),
                ToolExecutionTarget.Frontend);
        }

        return new ToolCatalog(tools);
    }

    public ToolExecutionTarget? GetTarget(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool.Target : null;

    public bool IsFrontend(string name) =>
        GetTarget(name) == ToolExecutionTarget.Frontend;

    private static JsonElement CloneSchema(JsonElement parameters) =>
        parameters.ValueKind == JsonValueKind.Object
            ? parameters.Clone()
            : JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new Dictionary<string, object>()
            });

    private sealed record CatalogedTool(LlmToolDefinition Definition, ToolExecutionTarget Target);
}
