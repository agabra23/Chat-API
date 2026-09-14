using ChatApi.Abstractions;
using ChatApi.Configuration;
using ChatApi.Conversations;
using ChatApi.Grounding;
using ChatApi.Providers;
using ChatApi.Services;
using ChatApi.Tools;
using Microsoft.Extensions.Options;

namespace ChatApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChatApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProviderOptions>(configuration.GetSection(ProviderOptions.SectionName));
        services.Configure<SearchOptions>(configuration.GetSection(SearchOptions.SectionName));
        services.Configure<ToolOptions>(configuration.GetSection(ToolOptions.SectionName));

        services.AddChatProviders();
        services.AddTools();

        services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        services.AddScoped<ChatService>();

        return services;
    }

    private static void AddChatProviders(this IServiceCollection services)
    {
        // Local Ollama.
        services.AddHttpClient<OllamaChatProvider>(c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<IChatProvider>(sp => sp.GetRequiredService<OllamaChatProvider>());

        // Anthropic (Claude Opus and friends).
        services.AddCloudProvider("anthropic", "ANTHROPIC_API_KEY",
            o => o.Anthropic,
            (http, opts) => new AnthropicChatProvider(http, opts));

        // OpenAI.
        services.AddCloudProvider("openai", "OPENAI_API_KEY",
            o => o.OpenAi,
            (http, opts) => new OpenAiCompatibleChatProvider("openai", http, opts));

        // Any other OpenAI-compatible gateway (OpenRouter, Groq, Together, vLLM...).
        services.AddCloudProvider("openai-compatible", "OPENAI_COMPATIBLE_API_KEY",
            o => o.OpenAiCompatible,
            (http, opts) => new OpenAiCompatibleChatProvider("openai-compatible", http, opts));

        services.AddSingleton<IChatProviderRegistry, ChatProviderRegistry>();
    }

    private static void AddCloudProvider(
        this IServiceCollection services,
        string name,
        string defaultApiKeyEnvVar,
        Func<ProviderOptions, CloudProviderOptions> selectOptions,
        Func<HttpClient, CloudProviderOptions, IChatProvider> factory)
    {
        services.AddHttpClient(name, c => c.Timeout = TimeSpan.FromMinutes(5));

        services.AddSingleton<IChatProvider>(sp =>
        {
            var options = selectOptions(sp.GetRequiredService<IOptions<ProviderOptions>>().Value);
            options.ApiKeyEnvVar ??= defaultApiKeyEnvVar;

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(name);
            return factory(http, options);
        });
    }

    private static void AddTools(this IServiceCollection services)
    {
        // Search backends available to the web_search tool.
        services.AddHttpClient<DuckDuckGoSearchProvider>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient<SearxngSearchProvider>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient<TavilySearchProvider>(c => c.Timeout = TimeSpan.FromSeconds(20));

        services.AddSingleton<IWebSearchProvider>(sp => sp.GetRequiredService<DuckDuckGoSearchProvider>());
        services.AddSingleton<IWebSearchProvider>(sp => sp.GetRequiredService<SearxngSearchProvider>());
        services.AddSingleton<IWebSearchProvider>(sp => sp.GetRequiredService<TavilySearchProvider>());

        // Register additional ITool implementations here to expand the model's capabilities.
        services.AddSingleton<ITool, WebSearchTool>();

        services.AddSingleton<IToolRegistry, ToolRegistry>();
    }
}
