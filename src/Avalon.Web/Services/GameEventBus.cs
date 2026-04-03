namespace Avalon.Web.Services;

/// <summary>
/// In-process event bus for notifying Blazor components of game state changes.
/// Singleton service — components subscribe per game ID.
/// </summary>
public class GameEventBus
{
    private readonly Dictionary<string, HashSet<Action>> _subscribers = new();
    private readonly object _lock = new();

    public void Subscribe(string gameId, Action callback)
    {
        lock (_lock)
        {
            if (!_subscribers.ContainsKey(gameId))
                _subscribers[gameId] = new HashSet<Action>();
            _subscribers[gameId].Add(callback);
        }
    }

    public void Unsubscribe(string gameId, Action callback)
    {
        lock (_lock)
        {
            if (_subscribers.TryGetValue(gameId, out var subs))
            {
                subs.Remove(callback);
                if (subs.Count == 0)
                    _subscribers.Remove(gameId);
            }
        }
    }

    public void Publish(string gameId)
    {
        Action[] callbacks;
        lock (_lock)
        {
            if (!_subscribers.TryGetValue(gameId, out var subs))
                return;
            callbacks = subs.ToArray();
        }

        foreach (var cb in callbacks)
        {
            try { cb(); }
            catch { /* swallow — component may be disposing */ }
        }
    }
}
