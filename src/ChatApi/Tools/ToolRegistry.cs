using ChatApi.Abstractions;

namespace ChatApi.Tools;

public sealed class ToolRegistry(IEnumerable<ITool> tools) : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools =
        tools.ToDictionary(t => t.Definition.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ITool> All => _tools.Values;

    public IReadOnlyList<ITool> Select(IReadOnlyCollection<string>? names)
    {
        if (names is null) return _tools.Values.ToList();

        return names
            .Select(n => _tools.TryGetValue(n, out var tool) ? tool : null)
            .OfType<ITool>()
            .ToList();
    }

    public bool TryGet(string name, out ITool tool) => _tools.TryGetValue(name, out tool!);
}
