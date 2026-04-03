using Avalon.Domain.Enums;

namespace Avalon.Domain.Models;

public class Quest
{
    public List<string> ParticipantIds { get; }
    public Dictionary<string, QuestVote> Votes { get; } = new();
    public bool? IsSuccess { get; private set; }
    public int SuccessCount => Votes.Values.Count(v => v == QuestVote.Success);
    public int FailCount => Votes.Values.Count(v => v == QuestVote.Fail);

    public Quest(List<string> participantIds)
    {
        ParticipantIds = participantIds ?? throw new ArgumentNullException(nameof(participantIds));
    }

    public void CastVote(string playerId, QuestVote vote)
    {
        if (!ParticipantIds.Contains(playerId))
            throw new InvalidOperationException($"Player {playerId} is not on this quest.");
        if (Votes.ContainsKey(playerId))
            throw new InvalidOperationException($"Player {playerId} has already voted.");
        Votes[playerId] = vote;
    }

    public void Resolve(int failsRequired)
    {
        if (Votes.Count != ParticipantIds.Count)
            throw new InvalidOperationException("Not all quest members have voted yet.");

        int fails = Votes.Values.Count(v => v == QuestVote.Fail);
        IsSuccess = fails < failsRequired;
    }
}
