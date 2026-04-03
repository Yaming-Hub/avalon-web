using Avalon.Domain.Enums;

namespace Avalon.Domain.Models;

public class Proposal
{
    public string LeaderPlayerId { get; }
    public List<string> ProposedPlayerIds { get; }
    public Dictionary<string, VoteType> Votes { get; } = new();
    public bool? IsApproved { get; private set; }

    public Proposal(string leaderPlayerId, List<string> proposedPlayerIds)
    {
        LeaderPlayerId = leaderPlayerId;
        ProposedPlayerIds = proposedPlayerIds ?? throw new ArgumentNullException(nameof(proposedPlayerIds));
    }

    public void CastVote(string playerId, VoteType vote)
    {
        if (Votes.ContainsKey(playerId))
            throw new InvalidOperationException($"Player {playerId} has already voted.");
        Votes[playerId] = vote;
    }

    public void Resolve(int totalPlayers)
    {
        if (Votes.Count != totalPlayers)
            throw new InvalidOperationException("Not all players have voted yet.");

        int approvals = Votes.Values.Count(v => v == VoteType.Approve);
        IsApproved = approvals > totalPlayers / 2;
    }
}
