using System.Collections.Concurrent;
using Avalon.Application.Interfaces;

namespace Avalon.Infrastructure.Persistence;

public class InMemoryActivityLog : IActivityLog
{
    private readonly ConcurrentDictionary<string, List<ActivityLogEntry>> _logs = new();
    private readonly ConcurrentDictionary<string, bool> _enabled = new();
    private readonly object _lock = new();

    public void EnableLogging(string gameId)
    {
        _enabled[gameId] = true;
    }

    public void DisableLogging(string gameId)
    {
        _enabled.TryRemove(gameId, out _);
    }

    public bool IsEnabled(string gameId)
    {
        return _enabled.TryGetValue(gameId, out var enabled) && enabled;
    }

    public void Log(string gameId, string category, string message, object? details = null)
    {
        if (!IsEnabled(gameId))
            return;

        var entry = new ActivityLogEntry
        {
            Timestamp = DateTime.UtcNow,
            GameId = gameId,
            Category = category,
            Message = message,
            Details = details,
        };

        _logs.AddOrUpdate(gameId,
            _ => new List<ActivityLogEntry> { entry },
            (_, list) => { lock (_lock) { list.Add(entry); } return list; });
    }

    public List<ActivityLogEntry> GetLogs(string gameId, int? limit = null)
    {
        if (!_logs.TryGetValue(gameId, out var logs))
            return new List<ActivityLogEntry>();

        lock (_lock)
        {
            if (limit.HasValue)
                return logs.TakeLast(limit.Value).ToList();
            return logs.ToList();
        }
    }

    public void Clear(string gameId)
    {
        _logs.TryRemove(gameId, out _);
    }
}
