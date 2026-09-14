using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatApi.Abstractions;
using ChatApi.Configuration;

namespace ChatApi.Providers;

/// <summary>
/// Works with any OpenAI-compatible /chat/completions endpoint:
/// OpenAI, OpenRouter, Groq, Together, vLLM, LM Studio, etc.
/// </summary>
public class OpenAiCompatibleChatProvider(
    string name,
    HttpClient http,
    CloudProviderOptions options) : IChatProvider
{
    public string Name => name;
    public string DefaultModel => options.DefaultModel;

    public async Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var baseUrl = (request.BaseUrl ?? options.BaseUrl).TrimEnd('/');
        var apiKey = request.ApiKey ?? ApiKeyResolver.Resolve(options)
            ?? throw new ProviderException(Name, "No API key configured. Set it in appsettings, an env var, or pass apiKey on the request.");

        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = request.Messages.Select(ToOpenAiMessage).ToList(),
            ["temperature"] = request.Temperature ?? 0.7,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = false
        };

        if (request.Tools.Count > 0)
        {
            payload["tools"] = request.Tools.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = t.ParametersSchema }
            }).ToList();
            payload["tool_choice"] = "auto";
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.SendAsync(message, cancellationToken);
        await ProviderHttp.EnsureSuccessAsync(response, Name, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<OpenAiResponse>(cancellationToken)
                   ?? throw new ProviderException(Name, "Empty response.");

        var choice = body.Choices?.FirstOrDefault();

        return new ChatCompletionResponse
        {
            Content = choice?.Message?.Content ?? string.Empty,
            Model = body.Model ?? request.Model,
            Provider = Name,
            ToolCalls = ExtractToolCalls(choice?.Message),
            PromptTokens = body.Usage?.PromptTokens,
            CompletionTokens = body.Usage?.CompletionTokens
        };
    }

    private static Dictionary<string, object?> ToOpenAiMessage(ChatMessage message)
    {
        if (message.Role == ChatMessage.Tool)
        {
            return new Dictionary<string, object?>
            {
                ["role"] = "tool",
                ["tool_call_id"] = message.ToolCallId,
                ["content"] = message.Content
            };
        }

        var result = new Dictionary<string, object?>
        {
            ["role"] = message.Role,
            ["content"] = message.Content
        };

        if (message.ToolCalls.Count > 0)
        {
            result["tool_calls"] = message.ToolCalls.Select(c => new
            {
                id = c.Id,
                type = "function",
                function = new { name = c.Name, arguments = c.Arguments.GetRawText() }
            }).ToList();
        }

        return result;
    }

    private static List<ToolCall> ExtractToolCalls(Msg? message)
    {
        if (message?.ToolCalls is not { Count: > 0 } calls) return [];

        var result = new List<ToolCall>();

        foreach (var call in calls)
        {
            if (call.Function?.Name is not { } toolName) continue;

            // OpenAI serializes arguments as a JSON *string*, so it needs a second parse.
            JsonElement arguments;
            try
            {
                arguments = JsonDocument.Parse(
                    string.IsNullOrWhiteSpace(call.Function.Arguments) ? "{}" : call.Function.Arguments)
                    .RootElement.Clone();
            }
            catch (JsonException)
            {
                arguments = JsonDocument.Parse("{}").RootElement.Clone();
            }

            result.Add(new ToolCall
            {
                Id = call.Id ?? Guid.NewGuid().ToString("n"),
                Name = toolName,
                Arguments = arguments
            });
        }

        return result;
    }

    private sealed record OpenAiResponse
    {
        [JsonPropertyName("model")] public string? Model { get; init; }
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; init; }
        [JsonPropertyName("usage")] public Usage? Usage { get; init; }
    }

    private sealed record Choice
    {
        [JsonPropertyName("message")] public Msg? Message { get; init; }
    }

    private sealed record Msg
    {
        [JsonPropertyName("content")] public string? Content { get; init; }
        [JsonPropertyName("tool_calls")] public List<OpenAiToolCall>? ToolCalls { get; init; }
    }

    private sealed record OpenAiToolCall
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("function")] public OpenAiFunction? Function { get; init; }
    }

    private sealed record OpenAiFunction
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("arguments")] public string? Arguments { get; init; }
    }

    private sealed record Usage
    {
        [JsonPropertyName("prompt_tokens")] public int? PromptTokens { get; init; }
        [JsonPropertyName("completion_tokens")] public int? CompletionTokens { get; init; }
    }
}

internal static class ApiKeyResolver
{
    public static string? Resolve(CloudProviderOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiKey)) return options.ApiKey;
        if (!string.IsNullOrWhiteSpace(options.ApiKeyEnvVar))
            return Environment.GetEnvironmentVariable(options.ApiKeyEnvVar);
        return null;
    }
}
