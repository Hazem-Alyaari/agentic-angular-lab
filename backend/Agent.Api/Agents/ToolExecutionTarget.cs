namespace Agent.Api.Agents;

/// <summary>
/// Explicit execution ownership. Never inferred from tool-name prefixes
/// or from "missing in ToolRegistry".
/// </summary>
public enum ToolExecutionTarget
{
    Server,
    Frontend
}
