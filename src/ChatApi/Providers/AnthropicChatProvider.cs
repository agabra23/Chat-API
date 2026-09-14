using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatApi.Abstractions;
using ChatApi.Configuration;

namespace ChatApi.Providers;

/// <summary>Anthropic Messages API (Claude Opus / Sonnet / Haiku), with tool-use support.</summary>
public sealed class AnthropicChatProvider(HttpClient http, CloudProviderOptions options) : IChatProvider
{
    private const string AnthropicVersion = "2023-06-01";

    public string Name => "anthropic";
    public string DefaultModel => options.DefaultModel;

    public async Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var baseUrl = (request.BaseUrl ?? options.BaseUrl).TrimEnd('/');
        var apiKey = request.ApiKey ?? ApiKeyResolver.Resolve(options)
            ?? throw new ProviderException(Name, "No API key configured. Set ANTHROPIC_API_KEY or pass apiKey on the request.");

        // Anthropic takes the system prompt out-of-band, not as a message.
        var system = string.Join("\n\n",
            request.Messages.Where(m => m.Role == ChatMessage.System).Select(m => m.Content));

        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = BuildMessages(request.Messages),
            ["max_tokens"] = request.MaxTokens ?? 2048,
            ["temperature"] = request.Temperature ?? 0.7
        };

        if (!string.IsNullOrWhiteSpace(system)) payload["system"] = system;

        if (request.Tools.Count > 0)
        {
            payload["tools"] = request.Tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                input_schema = t.ParametersSchema
            }).ToList();
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/messages")
        {
            Content = JsonContent.Create(payload)
        };
        message.Headers.Add("x-api-key", apiKey);
        message.Headers.Add("anthropic-version", AnthropicVersion);

        using var response = await http.SendAsync(message, cancellationToken);
        await ProviderHttp.EnsureSuccessAsync(response, Name, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<AnthropicResponse>(cancellationToken)
                   ?? throw new ProviderException(Name, "Empty response.");

        var blocks = body.Content ?? [];

        var text = string.Concat(blocks.Where(b => b.Type == "text").Select(b => b.Text));

        var toolCalls = blocks
            .Where(b => b is { Type: "tool_use", Name: not null })
            .Select(b => new ToolCall
            {
                Id = b.Id ?? Guid.NewGuid().ToString("n"),
                Name = b.Name!,
                Arguments = b.Input
            })
            .ToList();

        return new ChatCompletionResponse
        {
            Content = text,
            Model = body.Model ?? request.Model,
            Provider = Name,
            ToolCalls = toolCalls,
            PromptTokens = body.Usage?.InputTokens,
            CompletionTokens = body.Usage?.OutputTokens
        };
    }

    /// <summary>
    /// Anthropic expects tool results as <c>tool_result</c> blocks inside a *user* message,
    /// so consecutive tool messages are coalesced into one.
    /// </summary>
    private static List<object> BuildMessages(IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<object>();
        var pendingToolResults = new List<object>();

        void FlushToolResults()
        {
            if (pendingToolResults.Count == 0) return;
            result.Add(new { role = "user", content = pendingToolResults.ToList() });
            pendingToolResults.Clear();
        }

        foreach (var message in messages)
        {
            if (message.Role == ChatMessage.System) continue;

            if (message.Role == ChatMessage.Tool)
            {
                pendingToolResults.Add(new
                {
                    type = "tool_result",
                    tool_use_id = message.ToolCallId,
                    content = message.Content
                });
                continue;
            }

            FlushToolResults();

            if (message.Role == ChatMessage.Assistant && message.ToolCalls.Count > 0)
            {
                var blocks = new List<object>();

                if (!string.IsNullOrWhiteSpace(message.Content))
                    blocks.Add(new { type = "text", text = message.Content });

                blocks.AddRange(message.ToolCalls.Select(c => new
                {
                    type = "tool_use",
                    id = c.Id,
                    name = c.Name,
                    input = c.Arguments
                }));

                result.Add(new { role = "assistant", content = blocks });
                continue;
            }

            result.Add(new { role = message.Role, content = message.Content });
        }

        FlushToolResults();
        return result;
    }

    private sealed record AnthropicResponse
    {
        [JsonPropertyName("model")] public string? Model { get; init; }
        [JsonPropertyName("content")] public List<ContentBlock>? Content { get; init; }
        [JsonPropertyName("usage")] public AnthropicUsage? Usage { get; init; }
    }

    private sealed record ContentBlock
    {
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("text")] public string? Text { get; init; }
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("input")] public JsonElement Input { get; init; }
    }

    private sealed record AnthropicUsage
    {
        [JsonPropertyName("input_tokens")] public int? InputTokens { get; init; }
        [JsonPropertyName("output_tokens")] public int? OutputTokens { get; init; }
    }
}
