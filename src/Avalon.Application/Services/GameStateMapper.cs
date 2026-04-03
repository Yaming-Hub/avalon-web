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
            Rounds = game.Rounds.Select(r => MapRound(r, isGameOver)).ToList(),
        };

        if (player != null && gameStarted)
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
    };

    private static RoundView MapRound(Round round, bool isGameOver)
    {
        return new RoundView
        {
            RoundNumber = round.RoundNumber,
            RequiredTeamSize = round.RequiredTeamSize,
            FailsRequired = round.FailsRequired,
            IsSuccess = round.IsSuccess,
            Proposals = round.Proposals.Select(p => MapProposal(p, isGameOver || p.IsApproved.HasValue)).ToList(),
            Quest = round.Quest != null ? MapQuest(round.Quest, isGameOver || round.Quest.IsSuccess.HasValue) : null,
        };
    }

    private static ProposalView MapProposal(Proposal proposal, bool revealVotes)
    {
        return new ProposalView
        {
            LeaderPlayerId = proposal.LeaderPlayerId,
            ProposedPlayerIds = new List<string>(proposal.ProposedPlayerIds),
            Votes = revealVotes ? proposal.Votes.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()) : null,
            IsApproved = proposal.IsApproved,
        };
    }

    private static QuestView MapQuest(Quest quest, bool revealCounts)
    {
        return new QuestView
        {
            ParticipantIds = new List<string>(quest.ParticipantIds),
            SuccessCount = revealCounts ? quest.SuccessCount : null,
            FailCount = revealCounts ? quest.FailCount : null,
            IsSuccess = quest.IsSuccess,
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
}
