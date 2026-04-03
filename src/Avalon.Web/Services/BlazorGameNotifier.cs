using Microsoft.AspNetCore.SignalR;
using Avalon.Application.Interfaces;
using Avalon.Web.Hubs;

namespace Avalon.Web.Services;

/// <summary>
/// Game notifier that publishes to both the in-process event bus (for Blazor components)
/// and the SignalR hub (for external JS clients).
/// </summary>
public class BlazorGameNotifier : IGameNotifier
{
    private readonly GameEventBus _eventBus;
    private readonly IHubContext<GameHub> _hubContext;

    public BlazorGameNotifier(GameEventBus eventBus, IHubContext<GameHub> hubContext)
    {
        _eventBus = eventBus;
        _hubContext = hubContext;
    }

    public async Task NotifyPlayerJoined(string gameId, string playerName)
    {
        _eventBus.Publish(gameId);
        await _hubContext.Clients.Group(gameId).SendAsync("PlayerJoined", playerName);
    }

    public async Task NotifyPlayerLeft(string gameId, string playerName)
    {
        _eventBus.Publish(gameId);
        await _hubContext.Clients.Group(gameId).SendAsync("PlayerLeft", playerName);
    }

    public async Task NotifyGameStarted(string gameId)
    {
        _eventBus.Publish(gameId);
        await _hubContext.Clients.Group(gameId).SendAsync("GameStarted");
    }

    public async Task NotifyPhaseChanged(string gameId, string phase)
    {
        _eventBus.Publish(gameId);
        await _hubContext.Clients.Group(gameId).SendAsync("PhaseChanged", phase);
    }

    public async Task NotifyTeamProposed(string gameId, string leaderName, List<string> proposedPlayerNames)
    {
        _eventBus.Publish(gameId);
        await _hubContext.Clients.Group(gameId).SendAsync("TeamProposed", leaderName, proposedPlayerNames);
    }

    public async Task NotifyVoteRevealed(string gameId, Dictionary<string, string> votes)
    {
        _eventBus.Publish(gameId);
        await _hubContext.Clients.Group(gameId).SendAsync("VoteRevealed", votes);
    }

    public async Task NotifyQuestResult(string gameId, int successes, int fails)
    {
        _eventBus.Publish(gameId);
        await _hubContext.Clients.Group(gameId).SendAsync("QuestResult", successes, fails);
    }

    public async Task NotifyLadyResult(string connectionId, string targetAlignment)
    {
        _eventBus.Publish("__lady__");
        await _hubContext.Clients.Client(connectionId).SendAsync("LadyResult", targetAlignment);
    }

    public async Task NotifyGameOver(string gameId, string result)
    {
        _eventBus.Publish(gameId);
        await _hubContext.Clients.Group(gameId).SendAsync("GameOver", result);
    }
}
