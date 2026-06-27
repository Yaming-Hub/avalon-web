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
    private readonly IConversationService _chat;
    private readonly BotIntelligence _intelligence;
    private readonly Random _random = new();

    public BotService(IGameRepository repository, IGameNotifier notifier, IActivityLog log, IConversationService chat, BotIntelligence intelligence)
    {
        _repository = repository;
        _notifier = notifier;
        _log = log;
        _chat = chat;
        _intelligence = intelligence;
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

            // Notify all clients so their UIs refresh
            await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());
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

        int teamSize = game.CurrentRound!.RequiredTeamSize;
        List<string> teamIds;

        if (leader.Team == Team.Good)
        {
            // Good bot leader: include self + least suspicious others
            var suspicion = _intelligence.CalculateSuspicionScores(game, leader);
            var others = game.Players
                .Where(p => p.Id != leader.Id)
                .OrderBy(p => suspicion.GetValueOrDefault(p.Id, 0.0))
                .Take(teamSize - 1)
                .Select(p => p.Id)
                .ToList();
            teamIds = new List<string> { leader.Id };
            teamIds.AddRange(others);
        }
        else
        {
            // Evil bot leader: include self (evil) + fill with good players to look normal
            var goodPlayers = game.Players
                .Where(p => p.Id != leader.Id && p.Team == Team.Good)
                .OrderBy(_ => _random.Next())
                .Take(teamSize - 1)
                .Select(p => p.Id)
                .ToList();
            teamIds = new List<string> { leader.Id };
            teamIds.AddRange(goodPlayers);
            // Fill remaining if needed
            while (teamIds.Count < teamSize)
            {
                var extra = game.Players.First(p => !teamIds.Contains(p.Id));
                teamIds.Add(extra.Id);
            }
        }

        game.ProposeTeam(leader.Id, teamIds);
        return true;
    }

    private bool ProcessTeamVote(Game game)
    {
        var proposal = game.CurrentRound!.Proposals[^1];
        bool anyVoted = false;
        bool isEarlyGame = game.Rounds.Count <= 2;

        // Pre-calculate: how many evil bots are about to reject this proposal?
        // Evil bots coordinate to avoid multiple rejections in early rounds
        int evilBotsWantingToReject = 0;
        if (isEarlyGame)
        {
            bool hasEvil = proposal.ProposedPlayerIds
                .Any(id => game.Players.First(p => p.Id == id).Team == Team.Evil);
            if (!hasEvil)
            {
                evilBotsWantingToReject = game.Players
                    .Count(p => p.IsBot && p.Team == Team.Evil
                        && p.Id != proposal.LeaderPlayerId
                        && !proposal.Votes.ContainsKey(p.Id));
            }
        }

        int evilRejectionsAllowed = isEarlyGame ? 1 : 99; // early game: max 1 evil rejects
        int evilRejectionsSoFar = 0;

        foreach (var player in game.Players)
        {
            if (!player.IsBot) continue;
            if (player.Id == proposal.LeaderPlayerId) continue;
            if (proposal.Votes.ContainsKey(player.Id)) continue;

            var personality = BotPersonality.GetForBot(player.Name);
            VoteType vote;

            if (player.Team == Team.Evil)
            {
                bool hasEvil = proposal.ProposedPlayerIds
                    .Any(id => game.Players.First(p => p.Id == id).Team == Team.Evil);
                vote = hasEvil ? VoteType.Approve : VoteType.Reject;

                // Early game stealth: evil bots mostly approve to avoid detection
                if (isEarlyGame && !hasEvil)
                {
                    // Only allow one evil bot to reject; others must approve to blend in
                    if (evilRejectionsSoFar >= evilRejectionsAllowed)
                        vote = VoteType.Approve;
                    else if (_random.NextDouble() < personality.Deceptiveness * 0.7)
                        vote = VoteType.Approve; // deceptive evil bots approve to blend
                    
                    if (vote == VoteType.Reject)
                        evilRejectionsSoFar++;
                }
                else if (!isEarlyGame && !hasEvil)
                {
                    // Later game: boldness controls rejection frequency
                    if (_random.NextDouble() < (1.0 - personality.Boldness) * 0.4)
                        vote = VoteType.Approve;
                }

                // Random flip: even evil bots sometimes deviate to look human
                if (_random.NextDouble() < personality.Randomness * 0.15)
                    vote = vote == VoteType.Approve ? VoteType.Reject : VoteType.Approve;
            }
            else
            {
                // Good bot: use suspicion + personality
                var suspicion = _intelligence.CalculateSuspicionScores(game, player);
                double teamSuspicion = proposal.ProposedPlayerIds
                    .Sum(id => suspicion.GetValueOrDefault(id, 0.0));
                double avgSuspicion = teamSuspicion / proposal.ProposedPlayerIds.Count;

                // Early game: good bots are more trusting (less data available)
                double threshold = 2.0 - personality.Aggressiveness * 1.5;
                if (isEarlyGame) threshold += 1.0; // higher bar to reject early

                vote = avgSuspicion * personality.Analytical > threshold ? VoteType.Reject : VoteType.Approve;

                // Random flip: adds human-like unpredictability
                if (_random.NextDouble() < personality.Randomness * 0.25)
                    vote = vote == VoteType.Approve ? VoteType.Reject : VoteType.Approve;
            }

            game.VoteOnProposal(player.Id, vote);
            anyVoted = true;
        }

        // If proposal resolved after bot votes, post the result
        if (anyVoted && proposal.IsApproved.HasValue)
        {
            var approvers = proposal.Votes.Where(kv => kv.Value == VoteType.Approve)
                .Select(kv => game.Players.First(p => p.Id == kv.Key).Name).ToList();
            var rejecters = proposal.Votes.Where(kv => kv.Value == VoteType.Reject)
                .Select(kv => game.Players.First(p => p.Id == kv.Key).Name).ToList();
            var resultText = proposal.IsApproved.Value ? "✅ Team APPROVED" : "❌ Team REJECTED";
            var voteDetail = "";
            if (approvers.Count > 0) voteDetail += $"Approve: {string.Join(", ", approvers)}";
            if (rejecters.Count > 0) voteDetail += (voteDetail.Length > 0 ? " | " : "") + $"Reject: {string.Join(", ", rejecters)}";
            _chat.PostMessage(game.Id, "System", $"{resultText} ({approvers.Count}👍 {rejecters.Count}👎). {voteDetail}", isSystem: true);
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

            var personality = BotPersonality.GetForBot(player.Name);
            QuestVote vote;

            if (player.Team == Team.Evil)
            {
                // Evil bot: use Deceptiveness to decide whether to pass
                // Higher deceptiveness = more likely to vote Success to avoid detection
                int evilOnQuest = quest.ParticipantIds
                    .Count(id => game.Players.First(p => p.Id == id).Team == Team.Evil);

                // Strategically pass in early rounds or when multiple evil present
                bool earlyGame = game.Rounds.Count <= 2;
                double passChance = personality.Deceptiveness * (earlyGame ? 0.6 : 0.3);
                if (evilOnQuest >= 2) passChance += 0.2; // one can cover for the other

                vote = _random.NextDouble() < passChance ? QuestVote.Success : QuestVote.Fail;
            }
            else
            {
                // Good bot: normally vote Success, but Chaotic personality may rarely Fail
                // (e.g., to confuse evil about who's on their team, or test a theory)
                double failChance = personality.Randomness * 0.05; // very rare, max 3%
                vote = _random.NextDouble() < failChance ? QuestVote.Fail : QuestVote.Success;
            }

            game.VoteOnQuest(pid, vote);
            anyVoted = true;
        }

        // If quest resolved after bot votes, post the result
        if (anyVoted && quest.IsSuccess.HasValue)
        {
            var questResult = quest.IsSuccess.Value
                ? $"✅ Quest PASSED ({quest.SuccessCount} success, {quest.FailCount} fail)"
                : $"❌ Quest FAILED ({quest.SuccessCount} success, {quest.FailCount} fail)";
            _chat.PostMessage(game.Id, "System", questResult, isSystem: true);

            var goodWins = game.Rounds.Count(r => r.IsSuccess == true);
            var evilWins = game.Rounds.Count(r => r.IsSuccess == false);
            _chat.PostMessage(game.Id, "System", $"Score: Good {goodWins} — Evil {evilWins}", isSystem: true);
        }

        return anyVoted;
    }

    private bool ProcessQuestResult(Game game)
    {
        // Only auto-proceed if there are bot players in the game
        if (!game.Players.Any(p => p.IsBot))
            return false;

        game.ProceedFromQuestResult();

        // Post chat messages for the transition
        if (game.Phase == GamePhase.TeamProposal)
            _chat.PostMessage(game.Id, "System", $"📋 Round {game.CurrentRound?.RoundNumber} begins. {game.CurrentLeader?.Name} is the leader. Team size: {game.CurrentRound?.RequiredTeamSize}", isSystem: true);
        else if (game.Phase == GamePhase.AssassinVote)
            _chat.PostMessage(game.Id, "System", "⚔️ Good has won 3 quests! But the Assassin now has a chance to identify Merlin...", isSystem: true);
        else if (game.Phase == GamePhase.GameOver)
        {
            var resultMsg = game.Result switch
            {
                GameResult.GoodWins => "🎉 GOOD TEAM WINS! The forces of good have triumphed!",
                GameResult.EvilWins => "💀 EVIL TEAM WINS! The forces of darkness prevail!",
                _ => "Game over!"
            };
            _chat.PostMessage(game.Id, "System", resultMsg, isSystem: true);
        }

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

        // Use Merlin detection to pick the most likely Merlin
        var merlinScores = _intelligence.CalculateMerlinScores(game, assassin);
        Player target;

        if (merlinScores.Count > 0)
        {
            var targetId = merlinScores.OrderByDescending(kv => kv.Value).First().Key;
            target = game.Players.First(p => p.Id == targetId);
        }
        else
        {
            var goodTargets = game.Players.Where(p => p.Team == Team.Good).ToList();
            if (goodTargets.Count == 0) return false;
            target = goodTargets[_random.Next(goodTargets.Count)];
        }

        game.Assassinate(assassin.Id, target.Id);

        var resultMsg = game.Result switch
        {
            GameResult.EvilWinsByAssassination => $"🗡️ The Assassin targeted {target.Name} — it was Merlin! EVIL TEAM WINS by assassination!",
            GameResult.GoodWins => $"🗡️ The Assassin targeted {target.Name} — wrong guess! GOOD TEAM WINS!",
            _ => "Game over!"
        };
        _chat.PostMessage(game.Id, "System", resultMsg, isSystem: true);
        return true;
    }
}
