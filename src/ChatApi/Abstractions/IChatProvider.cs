namespace ChatApi.Abstractions;

/// <summary>
/// A pluggable LLM backend. Implement this to add a new model provider.
/// </summary>
public interface IChatProvider
{
    /// <summary>Stable key used to select this provider, e.g. "ollama", "openai", "anthropic".</summary>
    string Name { get; }

    /// <summary>Model used when the caller does not specify one.</summary>
    string DefaultModel { get; }

    Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves an <see cref="IChatProvider"/> by name.</summary>
public interface IChatProviderRegistry
{
    IChatProvider Resolve(string? name);
    IReadOnlyCollection<string> ProviderNames { get; }
}
