using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using ChatApi.Abstractions;

namespace ChatApi.Grounding;

/// <summary>
/// Zero-config web search using DuckDuckGo's HTML endpoint. No API key required,
/// which makes it a good default for local runs.
/// </summary>
public sealed partial class DuckDuckGoSearchProvider(HttpClient http) : IWebSearchProvider
{
    public string Name => "duckduckgo";

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken cancellationToken = default)
    {
        var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125 Safari/537.36");

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return Parse(html, maxResults);
    }

    private static List<SearchResult> Parse(string html, int maxResults)
    {
        var results = new List<SearchResult>();

        foreach (Match match in ResultBlockRegex().Matches(html))
        {
            if (results.Count >= maxResults) break;

            var href = WebUtility.HtmlDecode(match.Groups["url"].Value);
            var title = Clean(match.Groups["title"].Value);
            var snippet = Clean(match.Groups["snippet"].Value);

            var resolved = ResolveRedirect(href);
            if (string.IsNullOrWhiteSpace(resolved) || string.IsNullOrWhiteSpace(title)) continue;

            results.Add(new SearchResult(title, resolved, snippet));
        }

        return results;
    }

    /// <summary>DuckDuckGo wraps hits in /l/?uddg=&lt;encoded target&gt;.</summary>
    private static string ResolveRedirect(string href)
    {
        if (!href.Contains("uddg=", StringComparison.Ordinal)) return href;

        var queryStart = href.IndexOf('?');
        if (queryStart < 0) return href;

        var parsed = HttpUtility.ParseQueryString(href[(queryStart + 1)..]);
        return parsed["uddg"] ?? href;
    }

    private static string Clean(string raw) =>
        WebUtility.HtmlDecode(TagRegex().Replace(raw, string.Empty)).Trim();

    [GeneratedRegex(
        """<a[^>]*class="result__a"[^>]*href="(?<url>[^"]+)"[^>]*>(?<title>.*?)</a>.*?(?:class="result__snippet"[^>]*>(?<snippet>.*?)</a>)?""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ResultBlockRegex();

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();
}
