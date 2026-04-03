namespace Avalon.Domain.Models;

public class Round
{
    public int RoundNumber { get; }
    public int RequiredTeamSize { get; }
    public int FailsRequired { get; }
    public List<Proposal> Proposals { get; } = new();
    public Quest? Quest { get; private set; }
    public bool? IsSuccess => Quest?.IsSuccess;

    public Round(int roundNumber, int requiredTeamSize, int failsRequired)
    {
        RoundNumber = roundNumber;
        RequiredTeamSize = requiredTeamSize;
        FailsRequired = failsRequired;
    }

    public Proposal AddProposal(string leaderPlayerId, List<string> proposedPlayerIds)
    {
        var proposal = new Proposal(leaderPlayerId, proposedPlayerIds);
        Proposals.Add(proposal);
        return proposal;
    }

    public Quest StartQuest(List<string> participantIds)
    {
        Quest = new Quest(participantIds);
        return Quest;
    }
}
