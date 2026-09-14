namespace ChatApi.Abstractions;

/// <summary>Stores multi-turn conversation history.</summary>
public interface IConversationStore
{
    Task<IReadOnlyList<ChatMessage>> GetAsync(string conversationId, CancellationToken ct = default);
    Task AppendAsync(string conversationId, IEnumerable<ChatMessage> messages, CancellationToken ct = default);
    Task DeleteAsync(string conversationId, CancellationToken ct = default);
}
