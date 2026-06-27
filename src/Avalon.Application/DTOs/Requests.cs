namespace Avalon.Application.DTOs;

public record CreateGameRequest();

public record CreateGameResponse(string GameId, string HostPlayerId, string HostPlayerToken, string JoinLink);

public record JoinGameRequest(string PlayerName);

public record JoinGameResponse(string PlayerId, string PlayerToken);

public record UpdateSettingsRequest(
    bool MerlinEnabled,
    bool AssassinEnabled,
    bool PercivalEnabled,
    bool MorganaEnabled,
    bool MordredEnabled,
    bool OberonEnabled,
    bool LadyOfTheLakeEnabled,
    bool ActivityLogEnabled = false,
    double BotEvilBias = 0.0);

public record ProposeTeamRequest(List<string> PlayerIds);

public record VoteRequest(string Vote);

public record QuestVoteRequest(string Vote);

public record AssassinateRequest(string TargetPlayerId);

public record LadyInvestigateRequest(string TargetPlayerId);

public record ChatRequest(string Message);
