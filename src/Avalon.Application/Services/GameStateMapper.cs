using Avalon.Domain.Configuration;
using Avalon.Domain.Enums;
using Avalon.Domain.Models;
using Avalon.Application.DTOs;

namespace Avalon.Application.Services;

public class GameStateMapper
{
    public GameStateResponse MapToResponse(Game game, string? requestingPlayerId)
    {
        var isGameOver = game.Phase == GamePhase.GameOver;
        var player = requestingPlayerId != null
            ? game.Players.FirstOrDefault(p => p.Id == requestingPlayerId)
            : null;
        var gameStarted = game.Phase != GamePhase.Lobby;

        var response = new GameStateResponse
        {
            GameId = game.Id,
            Phase = game.Phase.ToString(),
            Result = game.Result?.ToString(),
            Settings = MapSettings(game.Settings),
            CurrentLeader = game.CurrentLeader?.Id,
            ConsecutiveRejections = game.ConsecutiveRejections,
            AssassinTarget = isGameOver ? game.AssassinTargetId : null,
            Players = game.Players.Select(p => MapPlayer(p, isGameOver)).ToList(),
            Rounds = game.Rounds.Select(r => MapRound(r, isGameOver, game.Players)).ToList(),
            VotesRequiredToApprove = gameStarted ? (game.Players.Count / 2) + 1 : null,
            HelpText = GetHelpText(game, player),
            LogsAccessed = game.LogsAccessed,
            ObserverPlayers = game.Observers.Select(p => MapPlayer(p, false)).ToList(),
        };

        // Check if requesting player is an observer
        var isObserver = requestingPlayerId != null
            && game.Observers.Any(o => o.Id == requestingPlayerId);
        response.IsObserver = isObserver;

        if (isObserver)
        {
            response.YourPlayerId = requestingPlayerId;
            response.HelpText = "You are observing this game. You can watch the actions and chat, but cannot participate until the next game starts.";
        }
        else if (player != null && gameStarted)
        {
            response.YourPlayerId = player.Id;
            response.YourRole = player.Role?.ToString();
            response.YourTeam = player.Team?.ToString();
            response.VisiblePlayerIds = game.GetVisiblePlayerIds(player.Id);
        }

        if (game.IsLadyOfTheLakeActive)
        {
            response.LadyOfTheLake = MapLadyOfTheLake(game.LadyOfTheLake!);
        }

        return response;
    }

    private static PlayerView MapPlayer(Player player, bool revealRoles)
    {
        return new PlayerView
        {
            Id = player.Id,
            Name = player.Name,
            IsHost = player.IsHost,
            IsBot = player.IsBot,
            Role = revealRoles ? player.Role?.ToString() : null,
            Team = revealRoles ? player.Team?.ToString() : null,
        };
    }

    private static GameSettingsView MapSettings(GameSettings settings) => new()
    {
        MerlinEnabled = settings.MerlinEnabled,
        AssassinEnabled = settings.AssassinEnabled,
        PercivalEnabled = settings.PercivalEnabled,
        MorganaEnabled = settings.MorganaEnabled,
        MordredEnabled = settings.MordredEnabled,
        OberonEnabled = settings.OberonEnabled,
        LadyOfTheLakeEnabled = settings.LadyOfTheLakeEnabled,
        ActivityLogEnabled = settings.ActivityLogEnabled,
    };

    private static RoundView MapRound(Round round, bool isGameOver, List<Player> allPlayers)
    {
        return new RoundView
        {
            RoundNumber = round.RoundNumber,
            RequiredTeamSize = round.RequiredTeamSize,
            FailsRequired = round.FailsRequired,
            IsSuccess = round.IsSuccess,
            Proposals = round.Proposals.Select(p => MapProposal(p, isGameOver || p.IsApproved.HasValue, allPlayers)).ToList(),
            Quest = round.Quest != null ? MapQuest(round.Quest, isGameOver || round.Quest.IsSuccess.HasValue) : null,
        };
    }

    private static ProposalView MapProposal(Proposal proposal, bool revealVotes, List<Player> allPlayers)
    {
        var votedIds = proposal.Votes.Keys.ToList();
        // All players must vote except the leader (who auto-approves)
        var allPlayerIds = allPlayers.Select(p => p.Id).ToList();
        var pendingIds = allPlayerIds.Where(id => !votedIds.Contains(id)).ToList();

        return new ProposalView
        {
            LeaderPlayerId = proposal.LeaderPlayerId,
            ProposedPlayerIds = new List<string>(proposal.ProposedPlayerIds),
            Votes = revealVotes ? proposal.Votes.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()) : null,
            IsApproved = proposal.IsApproved,
            VotedPlayerIds = votedIds,
            PendingPlayerIds = pendingIds,
        };
    }

    private static QuestView MapQuest(Quest quest, bool revealCounts)
    {
        var votedIds = quest.Votes.Keys.ToList();
        var pendingIds = quest.ParticipantIds.Where(id => !votedIds.Contains(id)).ToList();

        return new QuestView
        {
            ParticipantIds = new List<string>(quest.ParticipantIds),
            SuccessCount = revealCounts ? quest.SuccessCount : null,
            FailCount = revealCounts ? quest.FailCount : null,
            IsSuccess = quest.IsSuccess,
            VotedPlayerIds = votedIds,
            PendingPlayerIds = pendingIds,
        };
    }

    private static LadyOfTheLakeView MapLadyOfTheLake(LadyOfTheLake lady) => new()
    {
        CurrentHolderId = lady.CurrentHolderId,
        History = lady.InvestigationHistory.Select(h => new InvestigationView
        {
            InvestigatorId = h.InvestigatorId,
            TargetId = h.TargetId,
        }).ToList(),
    };

    private static string? GetHelpText(Game game, Player? player)
    {
        return game.Phase switch
        {
            GamePhase.Lobby =>
                "Welcome to Avalon! Wait for all players to join, then the host can configure settings and start the game. You need 5-10 players.",

            GamePhase.RoleReveal =>
                "Your secret role has been revealed. Remember your role and team. Look at the players you can see (if any). Do not reveal your role to others!",

            GamePhase.TeamProposal =>
                player?.Id == game.CurrentLeader?.Id
                    ? $"You are the leader! Select {game.CurrentRound?.RequiredTeamSize} players to go on the quest. You may include yourself. Choose wisely — your selection will be voted on by all players."
                    : $"The leader is selecting {game.CurrentRound?.RequiredTeamSize} players for the quest. Wait for the proposal, then you'll vote to approve or reject it.",

            GamePhase.TeamVote =>
                player?.Id == game.CurrentRound?.Proposals.LastOrDefault()?.LeaderPlayerId
                    ? "You proposed this team, so your approval is automatic. You can still change your vote to Reject if you change your mind. Waiting for all other players to vote."
                    : $"Vote to approve or reject the proposed team. A majority ({(game.Players.Count / 2) + 1} of {game.Players.Count}) must approve. If rejected {GameConfiguration.MaxConsecutiveRejections - game.ConsecutiveRejections} more time(s) in a row, evil wins!",

            GamePhase.Quest =>
                game.CurrentRound?.Quest?.ParticipantIds.Contains(player?.Id ?? "") == true
                    ? $"You are on this quest! Vote Success or Fail. The quest fails if {game.CurrentRound?.FailsRequired} or more Fail vote(s) are cast. Your individual vote is not revealed — only the totals."
                    : "The quest team is voting. Wait for all quest members to cast their votes. Only Success/Fail totals will be revealed.",

            GamePhase.QuestResult =>
                "The quest is complete! Review the results, then click Continue to proceed to the next round.",

            GamePhase.LadyOfTheLake =>
                game.LadyOfTheLake?.CurrentHolderId == player?.Id
                    ? "You hold the Lady of the Lake. Choose a player to investigate — you will secretly learn their team alignment (Good or Evil). The Lady then passes to that player."
                    : "The Lady of the Lake holder is investigating a player's alignment. Wait for the investigation to complete.",

            GamePhase.AssassinVote =>
                player?.Role == Role.Assassin
                    ? "Good has won 3 quests, but you can still win! Identify and assassinate Merlin. If you guess correctly, Evil wins!"
                    : "Good has won 3 quests! The Assassin is now trying to identify Merlin. If the Assassin guesses correctly, Evil still wins.",

            GamePhase.GameOver =>
                game.Result switch
                {
                    GameResult.GoodWins => "Good wins! The forces of good have triumphed. All roles are now revealed.",
                    GameResult.EvilWins => "Evil wins! The forces of evil have prevailed. All roles are now revealed.",
                    GameResult.EvilWinsByAssassination => "Evil wins by assassination! The Assassin correctly identified Merlin. All roles are now revealed.",
                    _ => "The game is over. All roles are now revealed."
                },

            _ => null
        };
    }
}
