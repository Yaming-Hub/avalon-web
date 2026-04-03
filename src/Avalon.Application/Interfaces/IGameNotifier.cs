namespace Avalon.Application.Interfaces;

/// <summary>
/// Abstraction for notifying connected clients of game events.
/// Implemented by SignalR in the Web layer.
/// </summary>
public interface IGameNotifier
{
    Task NotifyPlayerJoined(string gameId, string playerName);
    Task NotifyPlayerLeft(string gameId, string playerName);
    Task NotifyGameStarted(string gameId);
    Task NotifyPhaseChanged(string gameId, string phase);
    Task NotifyTeamProposed(string gameId, string leaderName, List<string> proposedPlayerNames);
    Task NotifyVoteRevealed(string gameId, Dictionary<string, string> votes);
    Task NotifyQuestResult(string gameId, int successes, int fails);
    Task NotifyLadyResult(string connectionId, string targetAlignment);
    Task NotifyGameOver(string gameId, string result);
}
