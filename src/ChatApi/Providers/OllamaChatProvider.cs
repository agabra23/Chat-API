using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatApi.Abstractions;
using ChatApi.Configuration;
using Microsoft.Extensions.Options;

namespace ChatApi.Providers;

/// <summary>Local Ollama backend (http://localhost:11434/api/chat), with tool-calling support.</summary>
public sealed class OllamaChatProvider(HttpClient http, IOptions<ProviderOptions> options) : IChatProvider
{
    private readonly OllamaOptions _opts = options.Value.Ollama;

    public string Name => "ollama";
    public string DefaultModel => _opts.DefaultModel;

    public async Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var baseUrl = (request.BaseUrl ?? _opts.BaseUrl).TrimEnd('/');

        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = request.Messages.Select(ToOllamaMessage).ToList(),
            ["stream"] = false,
            ["options"] = new { temperature = request.Temperature ?? 0.7, num_predict = request.MaxTokens }
        };

        if (request.Tools.Count > 0)
        {
            payload["tools"] = request.Tools.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = t.ParametersSchema }
            }).ToList();
        }

        using var response = await http.PostAsJsonAsync($"{baseUrl}/api/chat", payload, cancellationToken);
        await ProviderHttp.EnsureSuccessAsync(response, Name, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken)
                   ?? throw new ProviderException(Name, "Empty response from Ollama.");

        return new ChatCompletionResponse
        {
            Content = body.Message?.Content ?? string.Empty,
            Model = body.Model ?? request.Model,
            Provider = Name,
            ToolCalls = ExtractToolCalls(body.Message),
            PromptTokens = body.PromptEvalCount,
            CompletionTokens = body.EvalCount
        };
    }

    private static Dictionary<string, object?> ToOllamaMessage(ChatMessage message)
    {
        var result = new Dictionary<string, object?>
        {
            ["role"] = message.Role,
            ["content"] = message.Content
        };

        if (message.Role == ChatMessage.Tool && message.ToolName is not null)
            result["tool_name"] = message.ToolName;

        if (message.ToolCalls.Count > 0)
        {
            result["tool_calls"] = message.ToolCalls.Select(c => new
            {
                function = new { name = c.Name, arguments = c.Arguments }
            }).ToList();
        }

        return result;
    }

    /// <summary>Ollama does not emit call ids, so we synthesize stable ones per response.</summary>
    private static List<ToolCall> ExtractToolCalls(OllamaMessage? message)
    {
        if (message?.ToolCalls is not { Count: > 0 } calls) return [];

        return calls
            .Where(c => c.Function?.Name is not null)
            .Select((c, i) => new ToolCall
            {
                Id = $"call_{i}_{c.Function!.Name}",
                Name = c.Function.Name!,
                Arguments = c.Function.Arguments
            })
            .ToList();
    }

    private sealed record OllamaChatResponse
    {
        [JsonPropertyName("model")] public string? Model { get; init; }
        [JsonPropertyName("message")] public OllamaMessage? Message { get; init; }
        [JsonPropertyName("prompt_eval_count")] public int? PromptEvalCount { get; init; }
        [JsonPropertyName("eval_count")] public int? EvalCount { get; init; }
    }

    private sealed record OllamaMessage
    {
        [JsonPropertyName("role")] public string? Role { get; init; }
        [JsonPropertyName("content")] public string? Content { get; init; }
        [JsonPropertyName("tool_calls")] public List<OllamaToolCall>? ToolCalls { get; init; }
    }

    private sealed record OllamaToolCall
    {
        [JsonPropertyName("function")] public OllamaFunction? Function { get; init; }
    }

    private sealed record OllamaFunction
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("arguments")] public JsonElement Arguments { get; init; }
    }
}
