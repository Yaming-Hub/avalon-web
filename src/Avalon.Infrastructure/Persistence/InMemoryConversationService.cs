using System.Collections.Concurrent;
using Avalon.Application.Interfaces;

namespace Avalon.Infrastructure.Persistence;

public class InMemoryConversationService : IConversationService
{
    private readonly ConcurrentDictionary<string, List<ConversationMessage>> _messages = new();
    private readonly object _lock = new();

    public void PostMessage(string gameId, string sender, string message, bool isSystem = false)
    {
        var entry = new ConversationMessage
        {
            Timestamp = DateTime.UtcNow,
            GameId = gameId,
            Sender = sender,
            Message = message,
            IsSystem = isSystem,
        };

        _messages.AddOrUpdate(gameId,
            _ => { entry.Id = 1; return new List<ConversationMessage> { entry }; },
            (_, list) =>
            {
                lock (_lock)
                {
                    entry.Id = list.Count + 1;
                    list.Add(entry);
                }
                return list;
            });
    }

    public List<ConversationMessage> GetMessages(string gameId, int? since = null)
    {
        if (!_messages.TryGetValue(gameId, out var messages))
            return new List<ConversationMessage>();

        lock (_lock)
        {
            if (since.HasValue)
                return messages.Where(m => m.Id > since.Value).ToList();
            return messages.ToList();
        }
    }

    public int GetMessageCount(string gameId)
    {
        if (!_messages.TryGetValue(gameId, out var messages))
            return 0;
        lock (_lock) { return messages.Count; }
    }
}
