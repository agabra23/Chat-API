using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ChatApi.Abstractions;
using ChatApi.Configuration;
using Microsoft.Extensions.Options;

namespace ChatApi.Grounding;

/// <summary>Self-hosted SearXNG instance. Set Search:SearxngBaseUrl and Search:Provider=searxng.</summary>
public sealed class SearxngSearchProvider(HttpClient http, IOptions<SearchOptions> options) : IWebSearchProvider
{
    public string Name => "searxng";

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken cancellationToken = default)
    {
        var baseUrl = options.Value.SearxngBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/search?q={Uri.EscapeDataString(query)}&format=json";

        var body = await http.GetFromJsonAsync<SearxResponse>(url, cancellationToken);

        return body?.Results?
            .Take(maxResults)
            .Select(r => new SearchResult(r.Title ?? "", r.Url ?? "", r.Content ?? ""))
            .ToList() ?? [];
    }

    private sealed record SearxResponse
    {
        [JsonPropertyName("results")] public List<SearxResult>? Results { get; init; }
    }

    private sealed record SearxResult
    {
        [JsonPropertyName("title")] public string? Title { get; init; }
        [JsonPropertyName("url")] public string? Url { get; init; }
        [JsonPropertyName("content")] public string? Content { get; init; }
    }
}
