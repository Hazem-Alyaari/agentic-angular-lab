using Agent.Api.Llm;
using Agent.Api.Tools;

namespace Agent.Api.Agents;

public sealed class ToolRegistry(IEnumerable<IAgentTool> tools)
{
    private readonly Dictionary<string, IAgentTool> _tools = tools.ToDictionary(
        tool => tool.Name,
        StringComparer.Ordinal);

    public IReadOnlyList<LlmToolDefinition> GetDefinitions() =>
        _tools.Values.Select(tool => tool.ToDefinition()).ToList();

    public IAgentTool? Get(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;
}
