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
        var evilPlayers = game.Players.Where(p => p.Team == Team.Evil).ToList();

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

                    // Second-order: does this player consistently reject teams with specific evil players?
                    // Merlin would reject any team with ANY evil player (not just one specific one)
                    foreach (var evil in evilPlayers)
                    {
                        if (!proposal.ProposedPlayerIds.Contains(evil.Id)) continue;
                        if (vote == VoteType.Reject)
                            scores[player.Id] += 0.3; // rejects teams with this evil → Merlin-like
                        else
                            scores[player.Id] -= 0.2; // approves team with evil → less Merlin-like
                    }
                }
            }
        }

        // Consistency bonus: Merlin should reject ALL evil, not just some
        // Check if player's rejection pattern is broad (targets multiple evil)
        foreach (var player in goodPlayers)
        {
            int evilPlayersTargeted = 0;
            foreach (var evil in evilPlayers)
            {
                bool rejectedTeamWithThisEvil = game.Rounds.SelectMany(r => r.Proposals)
                    .Any(p => p.ProposedPlayerIds.Contains(evil.Id)
                        && p.Votes.TryGetValue(player.Id, out var v) && v == VoteType.Reject);
                if (rejectedTeamWithThisEvil) evilPlayersTargeted++;
            }
            // Targeting multiple evil players → strong Merlin signal
            if (evilPlayersTargeted >= 2)
                scores[player.Id] += evilPlayersTargeted * 0.8;
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
                scores[pid] += (double)failCount / round.Quest.ParticipantIds.Count * 3.0;
            }
        }

        // Analyze voting patterns
        foreach (var proposal in round.Proposals)
        {
            if (!proposal.IsApproved.HasValue) continue;

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

            // === Second-order reasoning ===
            // Analyze WHO each player rejects/approves to infer their knowledge

            if (bot.Team == Team.Good)
            {
                // "If Player X rejected a team, and that team later failed,
                //  then X might have good info → players X rejects are more suspicious"
                if (round.IsSuccess == false)
                {
                    // Find players who rejected the approved proposal that led to the failed quest
                    var approvedProposal = round.Proposals.FirstOrDefault(p => p.IsApproved == true);
                    if (approvedProposal != null)
                    {
                        var smartRejecters = approvedProposal.Votes
                            .Where(kv => kv.Value == VoteType.Reject && kv.Key != bot.Id)
                            .Select(kv => kv.Key)
                            .ToList();

                        // These rejecters had good instincts — trust their other rejections
                        foreach (var rejecterId in smartRejecters)
                        {
                            // Look at other proposals this player rejected
                            foreach (var otherProposal in round.Proposals)
                            {
                                if (otherProposal == approvedProposal) continue;
                                if (!otherProposal.Votes.TryGetValue(rejecterId, out var otherVote)) continue;
                                if (otherVote != VoteType.Reject) continue;

                                // Players on teams that smart-rejecters also rejected are suspicious
                                foreach (var suspectId in otherProposal.ProposedPlayerIds)
                                {
                                    if (suspectId == bot.Id || !scores.ContainsKey(suspectId)) continue;
                                    scores[suspectId] += 0.3;
                                }
                            }
                        }
                    }
                }

                // "If Player X consistently rejects teams with Player Y,
                //  and quests without Y succeed, Y is more suspicious"
                foreach (var targetPlayer in game.Players)
                {
                    if (targetPlayer.Id == bot.Id || !scores.ContainsKey(targetPlayer.Id)) continue;

                    int rejectionsWithTarget = 0;
                    int proposalsWithTarget = 0;

                    foreach (var r in game.Rounds)
                    {
                        foreach (var p in r.Proposals)
                        {
                            if (!p.ProposedPlayerIds.Contains(targetPlayer.Id)) continue;
                            if (!p.IsApproved.HasValue) continue;
                            proposalsWithTarget++;

                            // Count how many OTHER players rejected teams with this target
                            int othersRejecting = p.Votes
                                .Count(kv => kv.Key != bot.Id && kv.Key != targetPlayer.Id
                                    && kv.Value == VoteType.Reject);
                            if (othersRejecting > game.Players.Count / 3)
                                rejectionsWithTarget++;
                        }
                    }

                    // If many players frequently reject teams with target → suspicious
                    if (proposalsWithTarget >= 2 && rejectionsWithTarget > proposalsWithTarget / 2)
                        scores[targetPlayer.Id] += 0.8;
                }
            }
            else
            {
                // Evil bot second-order reasoning:
                // "If Player X rejects teams containing ME (evil), X might know I'm evil"
                // This is used in Merlin scoring, but also raises suspicion that X is Merlin
                foreach (var (voterId, vote) in proposal.Votes)
                {
                    if (voterId == bot.Id || !scores.ContainsKey(voterId)) continue;

                    if (vote == VoteType.Reject && proposal.ProposedPlayerIds.Contains(bot.Id))
                    {
                        // This player rejected a team with me → they might suspect me
                        // From evil perspective, this player might be Merlin or just smart
                        scores[voterId] += 0.4;
                    }
                    if (vote == VoteType.Approve && proposal.ProposedPlayerIds.Contains(bot.Id))
                    {
                        // Approved a team with me → less likely to be Merlin
                        scores[voterId] -= 0.3;
                    }
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
