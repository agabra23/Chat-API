using ChatApi.Abstractions;
using ChatApi.Configuration;
using ChatApi.Contracts;
using Microsoft.Extensions.Options;

namespace ChatApi.Services;

/// <summary>
/// Orchestrates a turn: load history -> call the model -> run any tools it requests -> repeat
/// until the model produces a final answer.
/// </summary>
public sealed class ChatService(
    IChatProviderRegistry providers,
    IConversationStore conversations,
    IToolRegistry tools,
    IOptions<ToolOptions> toolOptions,
    ILogger<ChatService> logger)
{
    private const string DefaultSystemPrompt =
        "You are a helpful assistant. Be accurate and concise. " +
        "You have tools available. Use them only when they are genuinely needed — " +
        "prefer answering directly when you already know the answer. " +
        "When you do use a tool, cite the sources it returns.";

    private readonly ToolOptions _toolOptions = toolOptions.Value;

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("message is required.", nameof(request));

        var conversationId = request.ConversationId ?? Guid.NewGuid().ToString("n");
        var provider = providers.Resolve(request.Provider);
        var model = request.Model ?? provider.DefaultModel;

        var history = await conversations.GetAsync(conversationId, cancellationToken);
        var isNewConversation = history.Count == 0;
        var userMessage = new ChatMessage(ChatMessage.User, request.Message);

        var working = new List<ChatMessage>();
        if (isNewConversation)
            working.Add(new ChatMessage(ChatMessage.System, request.SystemPrompt ?? DefaultSystemPrompt));
        working.AddRange(history);
        working.Add(userMessage);

        var enableTools = request.EnableTools ?? _toolOptions.Enabled;
        var activeTools = enableTools ? tools.Select(request.Tools) : [];
        var definitions = activeTools.Select(t => t.Definition).ToList();

        // Messages produced during this turn, persisted once the model settles on an answer.
        var turnMessages = new List<ChatMessage> { userMessage };
        var citations = new List<SearchResult>();
        var invocations = new List<ToolInvocationDto>();

        ChatCompletionResponse completion;
        var iteration = 0;

        while (true)
        {
            completion = await provider.CompleteAsync(new ChatCompletionRequest
            {
                Messages = working,
                Model = model,
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens,
                Tools = definitions,
                ApiKey = request.ApiKey,
                BaseUrl = request.BaseUrl
            }, cancellationToken);

            if (completion.ToolCalls.Count == 0) break;

            if (++iteration > _toolOptions.MaxIterations)
            {
                logger.LogWarning(
                    "Tool loop hit the {Max}-iteration cap for conversation {ConversationId}.",
                    _toolOptions.MaxIterations, conversationId);

                working.Add(new ChatMessage(ChatMessage.System,
                    "Tool call limit reached. Answer now using what you already have."));
                continue;
            }

            var assistantMessage = new ChatMessage(ChatMessage.Assistant, completion.Content)
            {
                ToolCalls = completion.ToolCalls
            };
            working.Add(assistantMessage);
            turnMessages.Add(assistantMessage);

            foreach (var call in completion.ToolCalls)
            {
                var result = await ExecuteToolAsync(call, cancellationToken);

                citations.AddRange(result.Citations);
                invocations.Add(new ToolInvocationDto(call.Name, call.Arguments));

                var toolMessage = ChatMessage.FromToolResult(call, result.Content);
                working.Add(toolMessage);
                turnMessages.Add(toolMessage);
            }
        }

        turnMessages.Add(new ChatMessage(ChatMessage.Assistant, completion.Content));

        var toPersist = new List<ChatMessage>();
        if (isNewConversation)
            toPersist.Add(new ChatMessage(ChatMessage.System, request.SystemPrompt ?? DefaultSystemPrompt));
        toPersist.AddRange(turnMessages);

        await conversations.AppendAsync(conversationId, toPersist, cancellationToken);

        return new ChatResponse
        {
            ConversationId = conversationId,
            Message = completion.Content,
            Provider = completion.Provider,
            Model = completion.Model,
            ToolCalls = invocations,
            Sources = Deduplicate(citations),
            Usage = new UsageDto(completion.PromptTokens, completion.CompletionTokens)
        };
    }

    private async Task<ToolResult> ExecuteToolAsync(ToolCall call, CancellationToken cancellationToken)
    {
        if (!tools.TryGet(call.Name, out var tool))
            return new ToolResult($"Error: unknown tool '{call.Name}'.");

        try
        {
            logger.LogInformation("Invoking tool {Tool} with {Arguments}", call.Name, call.Arguments);
            return await tool.ExecuteAsync(call, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Report the failure to the model rather than aborting the whole turn.
            logger.LogError(ex, "Tool {Tool} threw.", call.Name);
            return new ToolResult($"Error: tool '{call.Name}' failed: {ex.Message}");
        }
    }

    private static List<SourceDto> Deduplicate(List<SearchResult> citations) =>
        citations
            .GroupBy(c => c.Url)
            .Select(g => g.First())
            .Select((c, i) => new SourceDto(i + 1, c.Title, c.Url))
            .ToList();
}
