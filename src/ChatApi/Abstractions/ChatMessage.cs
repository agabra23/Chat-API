namespace ChatApi.Abstractions;

/// <summary>A single message in a conversation.</summary>
public sealed record ChatMessage(string Role, string Content)
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";

    /// <summary>Set on assistant messages when the model requested tool invocations.</summary>
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = [];

    /// <summary>Set on tool messages, linking the result back to the originating call.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>Set on tool messages: the name of the tool that produced this content.</summary>
    public string? ToolName { get; init; }

    public static ChatMessage FromToolResult(ToolCall call, string content) =>
        new(Tool, content) { ToolCallId = call.Id, ToolName = call.Name };
}

/// <summary>Provider-agnostic request passed to an <see cref="IChatProvider"/>.</summary>
public sealed record ChatCompletionRequest
{
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required string Model { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }

    /// <summary>Tools advertised to the model. Empty means a plain completion.</summary>
    public IReadOnlyList<ToolDefinition> Tools { get; init; } = [];

    /// <summary>Per-request override for the provider API key (cloud providers).</summary>
    public string? ApiKey { get; init; }

    /// <summary>Per-request override for the provider base URL.</summary>
    public string? BaseUrl { get; init; }
}

public sealed record ChatCompletionResponse
{
    public required string Content { get; init; }
    public required string Model { get; init; }
    public required string Provider { get; init; }

    /// <summary>Tool invocations requested by the model; empty when it produced a final answer.</summary>
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = [];

    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
}
