using System.Collections.Concurrent;
using ChatApi.Abstractions;

namespace ChatApi.Conversations;

/// <summary>
/// In-memory conversation history. Swap for Redis/EF by re-registering IConversationStore.
/// </summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _store = new();

    public Task<IReadOnlyList<ChatMessage>> GetAsync(string conversationId, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(conversationId, out var messages))
            return Task.FromResult<IReadOnlyList<ChatMessage>>([]);

        lock (messages)
        {
            return Task.FromResult<IReadOnlyList<ChatMessage>>(messages.ToList());
        }
    }

    public Task AppendAsync(string conversationId, IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        var list = _store.GetOrAdd(conversationId, _ => []);

        lock (list)
        {
            list.AddRange(messages);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string conversationId, CancellationToken ct = default)
    {
        _store.TryRemove(conversationId, out _);
        return Task.CompletedTask;
    }
}
