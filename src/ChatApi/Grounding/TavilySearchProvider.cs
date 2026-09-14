using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ChatApi.Abstractions;
using ChatApi.Configuration;
using Microsoft.Extensions.Options;

namespace ChatApi.Grounding;

/// <summary>Tavily search API — purpose-built for LLM grounding. Requires Search:TavilyApiKey.</summary>
public sealed class TavilySearchProvider(HttpClient http, IOptions<SearchOptions> options) : IWebSearchProvider
{
    public string Name => "tavily";

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken cancellationToken = default)
    {
        var apiKey = options.Value.TavilyApiKey
                     ?? Environment.GetEnvironmentVariable("TAVILY_API_KEY")
                     ?? throw new InvalidOperationException("Tavily selected but no API key configured.");

        var payload = new
        {
            api_key = apiKey,
            query,
            max_results = maxResults,
            search_depth = "basic"
        };

        using var response = await http.PostAsJsonAsync(
            "https://api.tavily.com/search", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TavilyResponse>(cancellationToken);

        return body?.Results?
            .Select(r => new SearchResult(r.Title ?? "", r.Url ?? "", r.Content ?? ""))
            .ToList() ?? [];
    }

    private sealed record TavilyResponse
    {
        [JsonPropertyName("results")] public List<TavilyResult>? Results { get; init; }
    }

    private sealed record TavilyResult
    {
        [JsonPropertyName("title")] public string? Title { get; init; }
        [JsonPropertyName("url")] public string? Url { get; init; }
        [JsonPropertyName("content")] public string? Content { get; init; }
    }
}
