using Microsoft.AspNetCore.SignalR;
using Avalon.Application.Interfaces;
using Avalon.Web.Hubs;

namespace Avalon.Web.Services;

public class SignalRGameNotifier : IGameNotifier
{
    private readonly IHubContext<GameHub> _hubContext;

    public SignalRGameNotifier(IHubContext<GameHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyPlayerJoined(string gameId, string playerName) =>
        _hubContext.Clients.Group(gameId).SendAsync("PlayerJoined", playerName);

    public Task NotifyPlayerLeft(string gameId, string playerName) =>
        _hubContext.Clients.Group(gameId).SendAsync("PlayerLeft", playerName);

    public Task NotifyGameStarted(string gameId) =>
        _hubContext.Clients.Group(gameId).SendAsync("GameStarted");

    public Task NotifyPhaseChanged(string gameId, string phase) =>
        _hubContext.Clients.Group(gameId).SendAsync("PhaseChanged", phase);

    public Task NotifyTeamProposed(string gameId, string leaderName, List<string> proposedPlayerNames) =>
        _hubContext.Clients.Group(gameId).SendAsync("TeamProposed", leaderName, proposedPlayerNames);

    public Task NotifyVoteRevealed(string gameId, Dictionary<string, string> votes) =>
        _hubContext.Clients.Group(gameId).SendAsync("VoteRevealed", votes);

    public Task NotifyQuestResult(string gameId, int successes, int fails) =>
        _hubContext.Clients.Group(gameId).SendAsync("QuestResult", successes, fails);

    public Task NotifyLadyResult(string connectionId, string targetAlignment) =>
        _hubContext.Clients.Client(connectionId).SendAsync("LadyResult", targetAlignment);

    public Task NotifyGameOver(string gameId, string result) =>
        _hubContext.Clients.Group(gameId).SendAsync("GameOver", result);
}
