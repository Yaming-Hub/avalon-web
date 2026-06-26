using Avalon.Domain.Enums;
using Avalon.Domain.Models;
using Avalon.Application.Interfaces;

namespace Avalon.Application.Services;

/// <summary>
/// Processes bot player actions based on simple fixed rules:
/// - Team Vote: Always Approve
/// - Quest Vote: Good bots vote Success, Evil bots vote Fail
/// - Team Proposal (if bot is leader): Pick random players
/// - Assassin Vote: Pick a random Good player
/// - Proceed from Quest Result: Auto-proceed
/// - Lady of the Lake: Investigate a random eligible player
/// </summary>
public class BotService
{
    private readonly IGameRepository _repository;
    private readonly IGameNotifier _notifier;
    private readonly IActivityLog _log;
    private readonly Random _random = new();

    public BotService(IGameRepository repository, IGameNotifier notifier, IActivityLog log)
    {
        _repository = repository;
        _notifier = notifier;
        _log = log;
    }

    /// <summary>
    /// Processes all pending bot actions for the current game phase.
    /// May trigger multiple phase transitions (e.g., all bots vote → quest resolves → proceed).
    /// </summary>
    public async Task ProcessBotActionsAsync(string gameId)
    {
        int maxIterations = 20;
        for (int i = 0; i < maxIterations; i++)
        {
            var game = await _repository.GetByIdAsync(gameId);
            if (game == null || game.Phase == GamePhase.GameOver || game.Phase == GamePhase.Lobby)
                return;

            _log.Log(gameId, "Bot", $"Processing bot actions (iteration {i + 1}, phase={game.Phase})");
            bool actionTaken = await ProcessOneRound(game);
            if (!actionTaken)
            {
                _log.Log(gameId, "Bot", "No bot actions needed");
                return;
            }

            await _repository.SaveAsync(game);
            _log.Log(gameId, "Bot", $"Bot action completed, new phase={game.Phase}");
        }
    }

    private Task<bool> ProcessOneRound(Game game)
    {
        var result = game.Phase switch
        {
            GamePhase.TeamProposal => ProcessTeamProposal(game),
            GamePhase.TeamVote => ProcessTeamVote(game),
            GamePhase.Quest => ProcessQuestVote(game),
            GamePhase.QuestResult => ProcessQuestResult(game),
            GamePhase.LadyOfTheLake => ProcessLadyOfTheLake(game),
            GamePhase.AssassinVote => ProcessAssassinVote(game),
            _ => false
        };
        return Task.FromResult(result);
    }

    private bool ProcessTeamProposal(Game game)
    {
        var leader = game.CurrentLeader;
        if (leader == null || !leader.IsBot)
            return false;

        // Bot leader picks random team
        int teamSize = game.CurrentRound!.RequiredTeamSize;
        var candidates = game.Players.OrderBy(_ => _random.Next()).Take(teamSize).Select(p => p.Id).ToList();
        game.ProposeTeam(leader.Id, candidates);
        return true;
    }

    private bool ProcessTeamVote(Game game)
    {
        var proposal = game.CurrentRound!.Proposals[^1];
        bool anyVoted = false;

        foreach (var player in game.Players)
        {
            if (!player.IsBot) continue;
            if (player.Id == proposal.LeaderPlayerId) continue; // leader auto-approved
            if (proposal.Votes.ContainsKey(player.Id)) continue;

            game.VoteOnProposal(player.Id, VoteType.Approve);
            anyVoted = true;
        }

        return anyVoted;
    }

    private bool ProcessQuestVote(Game game)
    {
        var quest = game.CurrentRound!.Quest!;
        bool anyVoted = false;

        foreach (var pid in quest.ParticipantIds)
        {
            if (quest.Votes.ContainsKey(pid)) continue;
            var player = game.Players.First(p => p.Id == pid);
            if (!player.IsBot) continue;

            var vote = player.Team == Team.Evil ? QuestVote.Fail : QuestVote.Success;
            game.VoteOnQuest(pid, vote);
            anyVoted = true;
        }

        return anyVoted;
    }

    private bool ProcessQuestResult(Game game)
    {
        // Only auto-proceed if there are bot players in the game
        if (!game.Players.Any(p => p.IsBot))
            return false;

        game.ProceedFromQuestResult();
        return true;
    }

    private bool ProcessLadyOfTheLake(Game game)
    {
        if (game.LadyOfTheLake == null) return false;

        var holderId = game.LadyOfTheLake.CurrentHolderId;
        var holder = game.Players.FirstOrDefault(p => p.Id == holderId);
        if (holder == null || !holder.IsBot) return false;

        var previousHolders = game.LadyOfTheLake.GetPreviousHolders();
        var validTargets = game.Players
            .Where(p => p.Id != holderId && !previousHolders.Contains(p.Id))
            .ToList();

        if (validTargets.Count == 0) return false;

        var target = validTargets[_random.Next(validTargets.Count)];
        game.InvestigateWithLady(holderId!, target.Id);
        return true;
    }

    private bool ProcessAssassinVote(Game game)
    {
        var assassin = game.Players.FirstOrDefault(p => p.Role == Role.Assassin);
        if (assassin == null || !assassin.IsBot) return false;

        // Pick a random good player (not the assassin themselves)
        var goodTargets = game.Players.Where(p => p.Team == Team.Good).ToList();
        if (goodTargets.Count == 0) return false;

        var target = goodTargets[_random.Next(goodTargets.Count)];
        game.Assassinate(assassin.Id, target.Id);
        return true;
    }
}
