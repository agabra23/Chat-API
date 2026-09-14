using System.Text;
using ChatApi.Abstractions;
using ChatApi.Configuration;
using Microsoft.Extensions.Options;

namespace ChatApi.Tools;

/// <summary>
/// Model-invoked web search. The model decides when a question needs live information
/// and calls this explicitly, rather than every turn being grounded unconditionally.
/// </summary>
public sealed class WebSearchTool(
    IEnumerable<IWebSearchProvider> searchProviders,
    IOptions<SearchOptions> options,
    ILogger<WebSearchTool> logger) : ITool
{
    public const string ToolName = "web_search";

    private readonly Dictionary<string, IWebSearchProvider> _providers =
        searchProviders.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    private readonly SearchOptions _options = options.Value;

    public ToolDefinition Definition => new()
    {
        Name = ToolName,
        Description =
            "Search the public web for current, factual, or niche information. " +
            "Use this when the answer depends on recent events, live data (prices, weather, releases, " +
            "people currently holding a role), or anything you are not confident about. " +
            "Do not use it for general knowledge, reasoning, math, or code you already know.",
        ParametersSchema = new
        {
            type = "object",
            properties = new
            {
                query = new
                {
                    type = "string",
                    description = "A focused search engine query. Prefer keywords over full sentences."
                },
                max_results = new
                {
                    type = "integer",
                    description = "How many results to return (1-10).",
                    minimum = 1,
                    maximum = 10
                }
            },
            required = new[] { "query" }
        }
    };

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        var query = call.GetString("query");
        if (string.IsNullOrWhiteSpace(query))
            return new ToolResult("Error: the 'query' argument is required.");

        var maxResults = Math.Clamp(call.GetInt("max_results") ?? _options.MaxResults, 1, 10);

        if (!_providers.TryGetValue(_options.Provider, out var provider))
        {
            logger.LogWarning("Search provider '{Provider}' is not registered.", _options.Provider);
            return new ToolResult($"Error: search provider '{_options.Provider}' is not available.");
        }

        try
        {
            var results = await provider.SearchAsync(query, maxResults, cancellationToken);

            return results.Count == 0
                ? new ToolResult($"No web results found for \"{query}\".")
                : new ToolResult(Format(query, results)) { Citations = results };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Surface failure to the model as text so it can recover or answer without search.
            logger.LogWarning(ex, "Web search failed for query '{Query}'.", query);
            return new ToolResult($"Error: web search failed ({ex.Message}). Answer from your own knowledge.");
        }
    }

    private static string Format(string query, IReadOnlyList<SearchResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Web results for \"{query}\" (retrieved {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC):");
        builder.AppendLine();

        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            builder.AppendLine($"[{i + 1}] {r.Title}");
            builder.AppendLine($"    URL: {r.Url}");
            if (!string.IsNullOrWhiteSpace(r.Snippet)) builder.AppendLine($"    {r.Snippet}");
            builder.AppendLine();
        }

        builder.AppendLine("Cite the results you rely on inline as [1], [2]. Never invent a citation.");
        return builder.ToString();
    }
}
