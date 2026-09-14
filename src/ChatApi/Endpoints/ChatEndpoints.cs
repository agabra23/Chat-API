using ChatApi.Abstractions;
using ChatApi.Contracts;
using ChatApi.Providers;
using ChatApi.Services;

namespace ChatApi.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/providers", (IChatProviderRegistry registry) =>
            Results.Ok(new { providers = registry.ProviderNames }));

        app.MapGet("/tools", (IToolRegistry tools) =>
            Results.Ok(new
            {
                tools = tools.All.Select(t => new { t.Definition.Name, t.Definition.Description })
            }));

        app.MapPost("/chat", async (
            ChatRequest request,
            ChatService chat,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await chat.ChatAsync(request, cancellationToken));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (ProviderException ex)
            {
                return Results.Problem(title: "Provider error", detail: ex.Message, statusCode: 502);
            }
        });

        app.MapGet("/conversations/{id}", async (
            string id, IConversationStore store, CancellationToken ct) =>
        {
            var messages = await store.GetAsync(id, ct);
            return messages.Count == 0
                ? Results.NotFound()
                : Results.Ok(new { conversationId = id, messages });
        });

        app.MapDelete("/conversations/{id}", async (
            string id, IConversationStore store, CancellationToken ct) =>
        {
            await store.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        return app;
    }
}
