namespace Agent.Api.Llm;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Provider API key. Prefer user-secrets or LLM_API_KEY / Llm__ApiKey.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model id, e.g. gpt-4o-mini.</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Optional OpenAI-compatible base URL.</summary>
    public string? BaseUrl { get; set; }

    public int TimeoutSeconds { get; set; } = 120;

    public int MaxToolIterations { get; set; } = 5;
}
