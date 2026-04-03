using Avalon.Domain.Models;

namespace Avalon.Application.Interfaces;

public interface IGameRepository
{
    Task<Game?> GetByIdAsync(string gameId);
    Task SaveAsync(Game game);
    Task DeleteAsync(string gameId);
    Task<bool> ExistsAsync(string gameId);
}
