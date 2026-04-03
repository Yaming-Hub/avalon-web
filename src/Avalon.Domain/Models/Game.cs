using Avalon.Domain.Configuration;
using Avalon.Domain.Enums;

namespace Avalon.Domain.Models;

public class Game
{
    public string Id { get; }
    public GamePhase Phase { get; private set; } = GamePhase.Lobby;
    public GameSettings Settings { get; private set; } = new();
    public List<Player> Players { get; } = new();
    public List<Round> Rounds { get; } = new();
    public int CurrentLeaderIndex { get; private set; }
    public int ConsecutiveRejections { get; private set; }
    public LadyOfTheLake? LadyOfTheLake { get; private set; }
    public GameResult? Result { get; private set; }
    public string? AssassinTargetId { get; private set; }
    public DateTime CreatedAt { get; }

    private readonly object _lock = new();
    private readonly RoleAssigner _roleAssigner;

    public Game(string id, RoleAssigner? roleAssigner = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        CreatedAt = DateTime.UtcNow;
        _roleAssigner = roleAssigner ?? new RoleAssigner();
    }

    // --- Derived properties ---
    public Round? CurrentRound => Rounds.Count > 0 ? Rounds[^1] : null;
    public Player? CurrentLeader => Players.Count > 0 ? Players[CurrentLeaderIndex % Players.Count] : null;
    public int CompletedQuestsGood => Rounds.Count(r => r.IsSuccess == true);
    public int CompletedQuestsEvil => Rounds.Count(r => r.IsSuccess == false);
    public bool IsLadyOfTheLakeActive => Settings.LadyOfTheLakeEnabled && LadyOfTheLake != null;

    // --- Lobby phase ---
    public Player Join(string playerName)
    {
        lock (_lock)
        {
            EnsurePhase(GamePhase.Lobby);

            if (Players.Count >= GameConfiguration.MaxPlayers)
                throw new InvalidOperationException($"Game is full (max {GameConfiguration.MaxPlayers} players).");

            if (Players.Any(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A player named '{playerName}' already exists.");

            bool isHost = Players.Count == 0;
            var player = new Player(Guid.NewGuid().ToString(), playerName, isHost);
            Players.Add(player);
            return player;
        }
    }

    public void RemovePlayer(string playerId)
    {
        lock (_lock)
        {
            EnsurePhase(GamePhase.Lobby);
            var player = GetPlayerOrThrow(playerId);
            if (player.IsHost)
                throw new InvalidOperationException("The host cannot be removed.");
            Players.Remove(player);
        }
    }

    public void UpdateSettings(string hostPlayerId, GameSettings newSettings)
    {
        lock (_lock)
        {
            EnsurePhase(GamePhase.Lobby);
            EnsureHost(hostPlayerId);
            Settings = newSettings ?? throw new ArgumentNullException(nameof(newSettings));
        }
    }

    // --- Game start ---
    public void Start(string hostPlayerId)
    {
        lock (_lock)
        {
            EnsurePhase(GamePhase.Lobby);
            EnsureHost(hostPlayerId);

            if (!GameConfiguration.IsValidPlayerCount(Players.Count))
                throw new InvalidOperationException(
                    $"Need {GameConfiguration.MinPlayers}-{GameConfiguration.MaxPlayers} players to start. Currently have {Players.Count}.");

            ValidateSettingsForPlayerCount();

            _roleAssigner.AssignRoles(Players, Settings);

            // Set initial leader randomly
            CurrentLeaderIndex = new Random().Next(Players.Count);

            // Initialize Lady of the Lake if enabled
            if (Settings.LadyOfTheLakeEnabled)
            {
                LadyOfTheLake = new LadyOfTheLake();
                // Lady starts with the player to the right of the first leader
                int ladyIndex = (CurrentLeaderIndex + Players.Count - 1) % Players.Count;
                LadyOfTheLake.Initialize(Players[ladyIndex].Id);
            }

            Phase = GamePhase.RoleReveal;
        }
    }

    public void ProceedFromRoleReveal()
    {
        lock (_lock)
        {
            EnsurePhase(GamePhase.RoleReveal);
            StartNewRound();
        }
    }

    // --- Team proposal ---
    public Proposal ProposeTeam(string playerId, List<string> proposedPlayerIds)
    {
        lock (_lock)
        {
            EnsurePhase(GamePhase.TeamProposal);

            if (CurrentLeader!.Id != playerId)
                throw new InvalidOperationException("Only the current leader can propose a team.");

            var round = CurrentRound!;
            if (proposedPlayerIds.Count != round.RequiredTeamSize)
                throw new InvalidOperationException(
                    $"Team must have exactly {round.RequiredTeamSize} players, got {proposedPlayerIds.Count}.");

            // Validate all proposed players exist and are unique
            var uniqueIds = proposedPlayerIds.Distinct().ToList();
            if (uniqueIds.Count != proposedPlayerIds.Count)
                throw new InvalidOperationException("Duplicate player IDs in proposal.");

            foreach (var pid in proposedPlayerIds)
                GetPlayerOrThrow(pid);

            var proposal = round.AddProposal(playerId, new List<string>(proposedPlayerIds));
            Phase = GamePhase.TeamVote;
            return proposal;
        }
    }

    // --- Team vote ---
    public void VoteOnProposal(string playerId, VoteType vote)
    {
        lock (_lock)
        {
            EnsurePhase(GamePhase.TeamVote);
            GetPlayerOrThrow(playerId);

            var proposal = CurrentRound!.Proposals[^1];
            proposal.CastVote(playerId, vote);

            if (proposal.Votes.Count == Players.Count)
            {
                proposal.Resolve(Players.Count);
                if (proposal.IsApproved == true)
                {
                    ConsecutiveRejections = 0;
                    var quest = CurrentRound!.StartQuest(new List<string>(proposal.ProposedPlayerIds));
                    Phase = GamePhase.Quest;
                }
                else
                {
                    ConsecutiveRejections++;
                    if (ConsecutiveRejections >= GameConfiguration.MaxConsecutiveRejections)
                    {
                        EndGame(GameResult.EvilWins);
                        return;
                    }

                    AdvanceLeader();
                    Phase = GamePhase.TeamProposal;
                }
            }
        }
    }

    // --- Quest voting ---
    public void VoteOnQuest(string playerId, QuestVote vote)
    {
        lock (_lock)
        {
            EnsurePhase(GamePhase.Quest);
            var player = GetPlayerOrThrow(playerId);

            // Good players must play success
            if (player.Team == Team.Good && vote == QuestVote.Fail)
                throw new InvalidOperationException("Good players must play Success.");

            var quest = CurrentRound!.Quest!;
            quest.CastVote(playerId, vote);

            if (quest.Votes.Count == quest.ParticipantIds.Count)
            {
                quest.Resolve(CurrentRound!.FailsRequired);
                Phase = GamePhase.QuestResult;
            }
        }
    }

    public void ProceedFromQuestResult()
    {
        lock (_lock)
        {
            EnsurePhase(GamePhase.QuestResult);

            // Check win conditions
            if (CompletedQuestsGood >= GameConfiguration.QuestsToWin)
            {
                if (Settings.AssassinEnabled && Players.Any(p => p.Role == Role.Merlin))
                {
                    Phase = GamePhase.AssassinVote;
                    return;
                }
                EndGame(GameResult.GoodWins);
                return;
            }

            if (CompletedQuestsEvil >= GameConfiguration.QuestsToWin)
            {
                EndGame(GameResult.EvilWins);
                return;
            }

            // Check Lady of the Lake (activates after quests 2, 3, 4)
            int completedRounds = Rounds.Count;
            if (IsLadyOfTheLakeActive && completedRounds >= 2 && completedRounds <= 4)
            {
                Phase = GamePhase.LadyOfTheLake;
                return;
            }

            AdvanceLeader();
            StartNewRound();
        }
    }

    // --- Lady of the Lake ---
    public Team InvestigateWithLady(string investigatorId, string targetId)
    {
        lock (_lock)
        {
            EnsurePhase(GamePhase.LadyOfTheLake);

            if (LadyOfTheLake == null)
                throw new InvalidOperationException("Lady of the Lake is not active.");

            if (LadyOfTheLake.CurrentHolderId != investigatorId)
                throw new InvalidOperationException("Only the Lady of the Lake holder can investigate.");

            if (investigatorId == targetId)
                throw new InvalidOperationException("Cannot investigate yourself.");

            var target = GetPlayerOrThrow(targetId);
            GetPlayerOrThrow(investigatorId);

            LadyOfTheLake.RecordInvestigation(investigatorId, targetId);

            AdvanceLeader();
            StartNewRound();

            return target.Team!.Value;
        }
    }

    // --- Assassination ---
    public void Assassinate(string assassinPlayerId, string targetPlayerId)
    {
        lock (_lock)
        {
            EnsurePhase(GamePhase.AssassinVote);

            var assassin = GetPlayerOrThrow(assassinPlayerId);
            if (assassin.Role != Role.Assassin)
                throw new InvalidOperationException("Only the Assassin can choose a target.");

            var target = GetPlayerOrThrow(targetPlayerId);
            if (target.Team != Team.Good)
                throw new InvalidOperationException("Assassin can only target Good players.");

            AssassinTargetId = targetPlayerId;

            if (target.Role == Role.Merlin)
                EndGame(GameResult.EvilWinsByAssassination);
            else
                EndGame(GameResult.GoodWins);
        }
    }

    // --- Role visibility (what each player can see) ---
    public List<string> GetVisiblePlayerIds(string playerId)
    {
        var player = GetPlayerOrThrow(playerId);
        if (Phase == GamePhase.Lobby || player.Role == null)
            return new List<string>();

        return player.Role switch
        {
            Role.Merlin => Players
                .Where(p => p.Id != playerId && p.Team == Team.Evil && p.Role != Role.Mordred)
                .Select(p => p.Id).ToList(),

            Role.Percival => Players
                .Where(p => p.Id != playerId && (p.Role == Role.Merlin || p.Role == Role.Morgana))
                .Select(p => p.Id).ToList(),

            Role.Assassin or Role.Morgana or Role.Mordred => Players
                .Where(p => p.Id != playerId && p.Team == Team.Evil && p.Role != Role.Oberon)
                .Select(p => p.Id).ToList(),

            Role.Oberon => new List<string>(),

            _ => new List<string>()
        };
    }

    // --- Helpers ---
    private void StartNewRound()
    {
        int roundNumber = Rounds.Count + 1;
        int teamSize = GameConfiguration.GetQuestTeamSize(Players.Count, roundNumber);
        int failsRequired = GameConfiguration.GetFailsRequired(Players.Count, roundNumber);
        Rounds.Add(new Round(roundNumber, teamSize, failsRequired));
        Phase = GamePhase.TeamProposal;
    }

    private void AdvanceLeader()
    {
        CurrentLeaderIndex = (CurrentLeaderIndex + 1) % Players.Count;
    }

    private void EndGame(GameResult result)
    {
        Result = result;
        Phase = GamePhase.GameOver;
    }

    private void EnsurePhase(GamePhase expected)
    {
        if (Phase != expected)
            throw new InvalidOperationException($"Invalid operation for phase {Phase}. Expected {expected}.");
    }

    private void EnsureHost(string playerId)
    {
        var player = GetPlayerOrThrow(playerId);
        if (!player.IsHost)
            throw new InvalidOperationException("Only the host can perform this action.");
    }

    private Player GetPlayerOrThrow(string playerId)
    {
        return Players.FirstOrDefault(p => p.Id == playerId)
            ?? throw new InvalidOperationException($"Player with ID '{playerId}' not found.");
    }

    private void ValidateSettingsForPlayerCount()
    {
        var (goodCount, evilCount) = GameConfiguration.GetTeamComposition(Players.Count);
        var (goodSpecial, evilSpecial) = Settings.CountSpecialRoles();

        if (goodSpecial > goodCount)
            throw new InvalidOperationException(
                $"Too many good special roles ({goodSpecial}) for {goodCount} good players.");
        if (evilSpecial > evilCount)
            throw new InvalidOperationException(
                $"Too many evil special roles ({evilSpecial}) for {evilCount} evil players.");
    }
}
