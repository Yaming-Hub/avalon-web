namespace Avalon.Domain.Models;

public class LadyOfTheLake
{
    public string? CurrentHolderId { get; private set; }
    public List<(string InvestigatorId, string TargetId)> InvestigationHistory { get; } = new();

    private readonly HashSet<string> _previousHolders = new();

    public void Initialize(string initialHolderId)
    {
        CurrentHolderId = initialHolderId;
        _previousHolders.Add(initialHolderId);
    }

    public bool CanInvestigate(string targetId)
    {
        return !_previousHolders.Contains(targetId);
    }

    public void RecordInvestigation(string investigatorId, string targetId)
    {
        if (investigatorId != CurrentHolderId)
            throw new InvalidOperationException("Only the current Lady of the Lake holder can investigate.");
        if (!CanInvestigate(targetId))
            throw new InvalidOperationException("Cannot investigate a player who has already held the Lady of the Lake.");

        InvestigationHistory.Add((investigatorId, targetId));
        _previousHolders.Add(targetId);
        CurrentHolderId = targetId;
    }

    public HashSet<string> GetPreviousHolders() => new(_previousHolders);
}
