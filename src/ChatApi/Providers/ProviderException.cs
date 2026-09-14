namespace ChatApi.Providers;

public sealed class ProviderException(string provider, string message)
    : Exception($"[{provider}] {message}")
{
    public string Provider { get; } = provider;
}

internal static class ProviderHttp
{
    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response, string provider, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new ProviderException(provider, $"HTTP {(int)response.StatusCode}: {body}");
    }
}
