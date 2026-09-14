using System.Text.Json;

namespace ChatApi.Abstractions;

/// <summary>A tool the model may choose to invoke.</summary>
public sealed record ToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>JSON Schema object describing the tool's parameters.</summary>
    public required object ParametersSchema { get; init; }
}

/// <summary>A model's request to invoke a tool.</summary>
public sealed record ToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Raw JSON arguments emitted by the model.</summary>
    public required JsonElement Arguments { get; init; }

    public string? GetString(string property) =>
        Arguments.ValueKind == JsonValueKind.Object
        && Arguments.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public int? GetInt(string property) =>
        Arguments.ValueKind == JsonValueKind.Object
        && Arguments.TryGetProperty(property, out var value)
        && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
}

/// <summary>Outcome of a tool invocation, fed back to the model.</summary>
public sealed record ToolResult(string Content)
{
    /// <summary>Optional sources surfaced to the API caller alongside the answer.</summary>
    public IReadOnlyList<SearchResult> Citations { get; init; } = [];
}

/// <summary>
/// A capability the model can call. Implement and register to extend what the assistant can do.
/// </summary>
public interface ITool
{
    ToolDefinition Definition { get; }

    Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default);
}

public interface IToolRegistry
{
    IReadOnlyCollection<ITool> All { get; }

    /// <summary>Returns the tools matching <paramref name="names"/>, or all enabled tools when null.</summary>
    IReadOnlyList<ITool> Select(IReadOnlyCollection<string>? names);

    bool TryGet(string name, out ITool tool);
}
