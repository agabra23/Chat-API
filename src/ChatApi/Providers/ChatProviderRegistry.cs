using ChatApi.Abstractions;
using ChatApi.Configuration;
using Microsoft.Extensions.Options;

namespace ChatApi.Providers;

public sealed class ChatProviderRegistry : IChatProviderRegistry
{
    private readonly Dictionary<string, IChatProvider> _providers;
    private readonly string _default;

    public ChatProviderRegistry(IEnumerable<IChatProvider> providers, IOptions<ProviderOptions> options)
    {
        _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        _default = options.Value.Default;
    }

    public IReadOnlyCollection<string> ProviderNames => _providers.Keys;

    public IChatProvider Resolve(string? name)
    {
        var key = string.IsNullOrWhiteSpace(name) ? _default : name;

        return _providers.TryGetValue(key, out var provider)
            ? provider
            : throw new ProviderException(key, $"Unknown provider. Available: {string.Join(", ", _providers.Keys)}");
    }
}
