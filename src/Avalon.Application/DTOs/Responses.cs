namespace Avalon.Application.DTOs;

public class GameStateResponse
{
    public string GameId { get; set; } = default!;
    public string Phase { get; set; } = default!;
    public string? Result { get; set; }
    public List<PlayerView> Players { get; set; } = new();
    public GameSettingsView Settings { get; set; } = default!;
    public List<RoundView> Rounds { get; set; } = new();
    public string? CurrentLeader { get; set; }
    public int ConsecutiveRejections { get; set; }
    public LadyOfTheLakeView? LadyOfTheLake { get; set; }

    // Player-specific info (only populated for the requesting player)
    public string? YourPlayerId { get; set; }
    public string? YourRole { get; set; }
    public string? YourTeam { get; set; }
    public List<string>? VisiblePlayerIds { get; set; }
    public string? AssassinTarget { get; set; }

    // Vote threshold info
    public int? VotesRequiredToApprove { get; set; }

    // Contextual help text for the current phase
    public string? HelpText { get; set; }

    // Indicates logs have been accessed — game integrity is compromised
    public bool LogsAccessed { get; set; }

    // Observer mode — player joined after game started, can watch but not act
    public bool IsObserver { get; set; }
    public List<PlayerView> ObserverPlayers { get; set; } = new();
}

public class PlayerView
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public bool IsHost { get; set; }
    public bool IsBot { get; set; }
    // Role and Team only visible under certain conditions
    public string? Role { get; set; }
    public string? Team { get; set; }
}

public class GameSettingsView
{
    public bool MerlinEnabled { get; set; }
    public bool AssassinEnabled { get; set; }
    public bool PercivalEnabled { get; set; }
    public bool MorganaEnabled { get; set; }
    public bool MordredEnabled { get; set; }
    public bool OberonEnabled { get; set; }
    public bool LadyOfTheLakeEnabled { get; set; }
    public bool ActivityLogEnabled { get; set; }
}

public class RoundView
{
    public int RoundNumber { get; set; }
    public int RequiredTeamSize { get; set; }
    public int FailsRequired { get; set; }
    public List<ProposalView> Proposals { get; set; } = new();
    public QuestView? Quest { get; set; }
    public bool? IsSuccess { get; set; }
}

public class ProposalView
{
    public string LeaderPlayerId { get; set; } = default!;
    public List<string> ProposedPlayerIds { get; set; } = new();
    public Dictionary<string, string>? Votes { get; set; }
    public bool? IsApproved { get; set; }
    // Track who has voted and who is still pending
    public List<string>? VotedPlayerIds { get; set; }
    public List<string>? PendingPlayerIds { get; set; }
}

public class QuestView
{
    public List<string> ParticipantIds { get; set; } = new();
    public int? SuccessCount { get; set; }
    public int? FailCount { get; set; }
    public bool? IsSuccess { get; set; }
    // Track who has voted and who is still pending (no individual votes revealed)
    public List<string>? VotedPlayerIds { get; set; }
    public List<string>? PendingPlayerIds { get; set; }
}

public class LadyOfTheLakeView
{
    public string? CurrentHolderId { get; set; }
    public List<InvestigationView> History { get; set; } = new();
}

public class InvestigationView
{
    public string InvestigatorId { get; set; } = default!;
    public string TargetId { get; set; } = default!;
}
