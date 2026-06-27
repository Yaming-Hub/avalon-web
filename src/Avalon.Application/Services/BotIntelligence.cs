using System.Collections.Concurrent;
using Avalon.Domain.Enums;
using Avalon.Domain.Models;

namespace Avalon.Application.Services;

/// <summary>
/// Bot intelligence engine that calculates suspicion scores and makes strategic decisions.
/// Maintains cross-game memory of player voting patterns.
/// </summary>
public class BotIntelligence
{
    // Cross-game memory: tracks player voting tendencies by name
    private readonly ConcurrentDictionary<string, PlayerProfile> _profiles = new();

    /// <summary>
    /// Calculate suspicion scores for all players from the perspective of a given bot.
    /// Higher score = more likely to be evil (from good bot's perspective)
    /// or more likely to be Merlin (from evil bot's perspective).
    /// </summary>
    public Dictionary<string, double> CalculateSuspicionScores(Game game, Player bot)
    {
        var scores = new Dictionary<string, double>();
        foreach (var player in game.Players)
        {
            if (player.Id == bot.Id) continue;
            scores[player.Id] = 0.0;
        }

        // Analyze completed rounds
        foreach (var round in game.Rounds)
        {
            AnalyzeRoundForSuspicion(game, bot, round, scores);
        }

        // Factor in cross-game memory
        ApplyCrossGameMemory(game, bot, scores);

        return scores;
    }

    /// <summary>
    /// For evil bots: estimate which good player is most likely Merlin.
    /// Merlin tends to reject teams containing evil players and approve teams without evil.
    /// </summary>
    public Dictionary<string, double> CalculateMerlinScores(Game game, Player evilBot)
    {
        var scores = new Dictionary<string, double>();
        var goodPlayers = game.Players.Where(p => p.Team == Team.Good).ToList();

        foreach (var player in goodPlayers)
            scores[player.Id] = 0.0;

        foreach (var round in game.Rounds)
        {
            foreach (var proposal in round.Proposals)
            {
                if (!proposal.IsApproved.HasValue) continue;

                // Count how many known evil are in this proposal
                int evilInTeam = proposal.ProposedPlayerIds
                    .Count(id => game.Players.First(p => p.Id == id).Team == Team.Evil);

                foreach (var player in goodPlayers)
                {
                    if (!proposal.Votes.ContainsKey(player.Id)) continue;
                    var vote = proposal.Votes[player.Id];

                    if (evilInTeam > 0 && vote == VoteType.Reject)
                    {
                        // Correctly rejecting evil teams → likely has info (Merlin behavior)
                        scores[player.Id] += 1.5 * evilInTeam;
                    }
                    else if (evilInTeam == 0 && vote == VoteType.Approve)
                    {
                        // Approving clean teams → subtle Merlin signal
                        scores[player.Id] += 0.5;
                    }
                    else if (evilInTeam > 0 && vote == VoteType.Approve)
                    {
                        // Approving evil teams → less likely Merlin
                        scores[player.Id] -= 1.0;
                    }
                    else if (evilInTeam == 0 && vote == VoteType.Reject)
                    {
                        // Rejecting clean teams → less likely Merlin (random or confused)
                        scores[player.Id] -= 0.3;
                    }
                }
            }
        }

        // Factor in cross-game memory
        foreach (var player in goodPlayers)
        {
            var profile = GetProfile(player.Name);
            // Players who historically detect evil well might be Merlin-types
            scores[player.Id] += profile.EvilDetectionRate * 2.0;
        }

        return scores;
    }

    /// <summary>
    /// Record the outcome of a game for cross-game learning.
    /// </summary>
    public void RecordGameResult(Game game)
    {
        foreach (var player in game.Players)
        {
            if (player.IsBot) continue; // Only track human players

            var profile = GetOrCreateProfile(player.Name);

            // Track voting accuracy: did they correctly identify evil?
            int correctRejections = 0;
            int totalVotes = 0;

            foreach (var round in game.Rounds)
            {
                foreach (var proposal in round.Proposals)
                {
                    if (!proposal.Votes.ContainsKey(player.Id)) continue;
                    totalVotes++;

                    int evilInTeam = proposal.ProposedPlayerIds
                        .Count(id => game.Players.FirstOrDefault(p => p.Id == id)?.Team == Team.Evil);

                    var vote = proposal.Votes[player.Id];
                    if (evilInTeam > 0 && vote == VoteType.Reject)
                        correctRejections++;
                }
            }

            if (totalVotes > 0)
            {
                double accuracy = (double)correctRejections / totalVotes;
                // Exponential moving average
                profile.EvilDetectionRate = profile.EvilDetectionRate * 0.7 + accuracy * 0.3;
            }

            profile.GamesPlayed++;
            profile.LastRole = player.Role?.ToString();
            profile.LastTeam = player.Team?.ToString();

            // Track approval tendency
            int approvals = game.Rounds.SelectMany(r => r.Proposals)
                .Count(p => p.Votes.TryGetValue(player.Id, out var v) && v == VoteType.Approve);
            int rejections = game.Rounds.SelectMany(r => r.Proposals)
                .Count(p => p.Votes.TryGetValue(player.Id, out var v) && v == VoteType.Reject);

            if (approvals + rejections > 0)
            {
                double approvalRate = (double)approvals / (approvals + rejections);
                profile.ApprovalRate = profile.ApprovalRate * 0.7 + approvalRate * 0.3;
            }
        }
    }

    public PlayerProfile GetProfile(string playerName)
    {
        return _profiles.GetOrAdd(playerName, _ => new PlayerProfile { Name = playerName });
    }

    private PlayerProfile GetOrCreateProfile(string playerName)
    {
        return _profiles.GetOrAdd(playerName, _ => new PlayerProfile { Name = playerName });
    }

    private void AnalyzeRoundForSuspicion(Game game, Player bot, Round round, Dictionary<string, double> scores)
    {
        // If a quest failed, players on that quest are more suspicious
        if (round.Quest != null && round.IsSuccess == false)
        {
            int failCount = round.Quest.FailCount;
            foreach (var pid in round.Quest.ParticipantIds)
            {
                if (pid == bot.Id) continue;
                if (!scores.ContainsKey(pid)) continue;

                // Each fail makes quest participants more suspicious
                // But spread suspicion based on how many were on the quest
                scores[pid] += (double)failCount / round.Quest.ParticipantIds.Count * 3.0;
            }
        }

        // Analyze voting patterns
        foreach (var proposal in round.Proposals)
        {
            if (!proposal.IsApproved.HasValue) continue;

            int evilInTeam = 0;
            if (bot.Team == Team.Evil)
            {
                // Evil bot knows who's evil
                evilInTeam = proposal.ProposedPlayerIds
                    .Count(id => game.Players.First(p => p.Id == id).Team == Team.Evil);
            }

            foreach (var (voterId, vote) in proposal.Votes)
            {
                if (voterId == bot.Id || !scores.ContainsKey(voterId)) continue;

                if (bot.Team == Team.Good)
                {
                    // Good bot: players who frequently approve failed quests are suspicious
                    if (round.IsSuccess == false && vote == VoteType.Approve)
                        scores[voterId] += 0.5;
                    // Players who reject everything are slightly less suspicious
                    if (vote == VoteType.Reject)
                        scores[voterId] -= 0.2;
                }
            }
        }
    }

    private void ApplyCrossGameMemory(Game game, Player bot, Dictionary<string, double> scores)
    {
        foreach (var player in game.Players)
        {
            if (player.Id == bot.Id || player.IsBot) continue;
            if (!scores.ContainsKey(player.Id)) continue;

            var profile = GetProfile(player.Name);
            if (profile.GamesPlayed == 0) continue;

            if (bot.Team == Team.Good)
            {
                // High approval rate across games → slightly suspicious (evil tends to approve more)
                if (profile.ApprovalRate > 0.7)
                    scores[player.Id] += 0.5;
            }
        }
    }
}

public class PlayerProfile
{
    public string Name { get; set; } = default!;
    public int GamesPlayed { get; set; }
    public double EvilDetectionRate { get; set; } = 0.5; // 0-1, how well they detect evil
    public double ApprovalRate { get; set; } = 0.5; // 0-1, how often they approve
    public string? LastRole { get; set; }
    public string? LastTeam { get; set; }
}
