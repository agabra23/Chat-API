using System.Text.Json;

namespace ChatApi.Contracts;

/// <summary>POST /chat payload.</summary>
public sealed record ChatRequest
{
    /// <summary>The user's message. Required.</summary>
    public string Message { get; init; } = "";

    /// <summary>Omit to start a new conversation; pass the returned id to continue it.</summary>
    public string? ConversationId { get; init; }

    /// <summary>"ollama" (default), "anthropic", "openai", or "openai-compatible".</summary>
    public string? Provider { get; init; }

    /// <summary>Model id. Falls back to the provider's configured default.</summary>
    public string? Model { get; init; }

    /// <summary>Optional system prompt; applied on the first turn of a conversation.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Set false to withhold all tools from the model for this request.</summary>
    public bool? EnableTools { get; init; }

    /// <summary>Restrict which tools are offered, e.g. ["web_search"]. Null offers all.</summary>
    public IReadOnlyCollection<string>? Tools { get; init; }

    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }

    /// <summary>Bring-your-own key, so no secrets need to live in config.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Override the provider base URL (e.g. a remote Ollama host).</summary>
    public string? BaseUrl { get; init; }
}

public sealed record ChatResponse
{
    public required string ConversationId { get; init; }
    public required string Message { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }

    /// <summary>Tools the model chose to invoke during this turn.</summary>
    public required IReadOnlyList<ToolInvocationDto> ToolCalls { get; init; }

    /// <summary>Sources returned by tools, if any.</summary>
    public required IReadOnlyList<SourceDto> Sources { get; init; }

    public UsageDto? Usage { get; init; }
}

public sealed record ToolInvocationDto(string Name, JsonElement Arguments);

public sealed record SourceDto(int Index, string Title, string Url);

public sealed record UsageDto(int? PromptTokens, int? CompletionTokens);
