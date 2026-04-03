using System.Collections.Concurrent;
using Avalon.Application.Interfaces;
using Avalon.Domain.Models;

namespace Avalon.Infrastructure.Persistence;

public class InMemoryGameRepository : IGameRepository
{
    private readonly ConcurrentDictionary<string, Game> _games = new();

    public Task<Game?> GetByIdAsync(string gameId)
    {
        _games.TryGetValue(gameId, out var game);
        return Task.FromResult(game);
    }

    public Task SaveAsync(Game game)
    {
        _games.AddOrUpdate(game.Id, game, (_, _) => game);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string gameId)
    {
        _games.TryRemove(gameId, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string gameId)
    {
        return Task.FromResult(_games.ContainsKey(gameId));
    }
}
