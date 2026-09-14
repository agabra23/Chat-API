namespace ChatApi.Abstractions;

public sealed record SearchResult(string Title, string Url, string Snippet);

/// <summary>A pluggable web search backend used for grounding.</summary>
public interface IWebSearchProvider
{
    string Name { get; }

    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default);
}
