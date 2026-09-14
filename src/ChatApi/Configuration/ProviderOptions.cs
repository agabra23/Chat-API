namespace ChatApi.Configuration;

public sealed class ProviderOptions
{
    public const string SectionName = "Providers";

    public string Default { get; set; } = "ollama";
    public OllamaOptions Ollama { get; set; } = new();
    public CloudProviderOptions OpenAi { get; set; } = new()
    {
        BaseUrl = "https://api.openai.com/v1",
        DefaultModel = "gpt-4o-mini"
    };
    public CloudProviderOptions Anthropic { get; set; } = new()
    {
        BaseUrl = "https://api.anthropic.com/v1",
        DefaultModel = "claude-opus-4-1"
    };
    /// <summary>Any OpenAI-compatible endpoint (Groq, Together, OpenRouter, vLLM, LM Studio...).</summary>
    public CloudProviderOptions OpenAiCompatible { get; set; } = new()
    {
        BaseUrl = "https://openrouter.ai/api/v1",
        DefaultModel = "openai/gpt-4o-mini"
    };
}

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string DefaultModel { get; set; } = "llama3.1";
}

public sealed class CloudProviderOptions
{
    public string BaseUrl { get; set; } = "";
    public string DefaultModel { get; set; } = "";
    public string? ApiKey { get; set; }
    /// <summary>Environment variable consulted when <see cref="ApiKey"/> is not set.</summary>
    public string? ApiKeyEnvVar { get; set; }
}

public sealed class SearchOptions
{
    public const string SectionName = "Search";

    public string Provider { get; set; } = "duckduckgo";
    public int MaxResults { get; set; } = 5;
    public string SearxngBaseUrl { get; set; } = "http://localhost:8080";
    public string? TavilyApiKey { get; set; }
}

public sealed class ToolOptions
{
    public const string SectionName = "Tools";

    /// <summary>Whether tools are advertised to the model by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Safety bound on consecutive tool round-trips within a single turn.</summary>
    public int MaxIterations { get; set; } = 4;
}
