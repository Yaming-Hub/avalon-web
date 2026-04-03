namespace Avalon.Domain.Configuration;

/// <summary>
/// Static game configuration tables for Avalon based on player count.
/// </summary>
public static class GameConfiguration
{
    /// <summary>
    /// Quest team sizes indexed by [playerCount][questNumber].
    /// Player counts 5-10, quests 1-5.
    /// </summary>
    private static readonly Dictionary<int, int[]> QuestTeamSizes = new()
    {
        { 5,  new[] { 2, 3, 2, 3, 3 } },
        { 6,  new[] { 2, 3, 4, 3, 4 } },
        { 7,  new[] { 2, 3, 3, 4, 4 } },
        { 8,  new[] { 3, 4, 4, 5, 5 } },
        { 9,  new[] { 3, 4, 4, 5, 5 } },
        { 10, new[] { 3, 4, 4, 5, 5 } }
    };

    /// <summary>
    /// Team composition: (goodCount, evilCount) by player count.
    /// </summary>
    private static readonly Dictionary<int, (int Good, int Evil)> TeamComposition = new()
    {
        { 5,  (3, 2) },
        { 6,  (4, 2) },
        { 7,  (4, 3) },
        { 8,  (5, 3) },
        { 9,  (6, 3) },
        { 10, (6, 4) }
    };

    public const int MinPlayers = 5;
    public const int MaxPlayers = 10;
    public const int TotalQuests = 5;
    public const int QuestsToWin = 3;
    public const int MaxConsecutiveRejections = 5;

    /// <summary>
    /// For 7+ players, quest 4 requires 2 fail votes to fail.
    /// </summary>
    public static int GetFailsRequired(int playerCount, int questNumber)
    {
        if (playerCount >= 7 && questNumber == 4)
            return 2;
        return 1;
    }

    public static int GetQuestTeamSize(int playerCount, int questNumber)
    {
        ValidatePlayerCount(playerCount);
        if (questNumber < 1 || questNumber > TotalQuests)
            throw new ArgumentOutOfRangeException(nameof(questNumber), $"Quest number must be between 1 and {TotalQuests}.");
        return QuestTeamSizes[playerCount][questNumber - 1];
    }

    public static int[] GetAllQuestTeamSizes(int playerCount)
    {
        ValidatePlayerCount(playerCount);
        return (int[])QuestTeamSizes[playerCount].Clone();
    }

    public static (int Good, int Evil) GetTeamComposition(int playerCount)
    {
        ValidatePlayerCount(playerCount);
        return TeamComposition[playerCount];
    }

    public static bool IsValidPlayerCount(int playerCount) =>
        playerCount >= MinPlayers && playerCount <= MaxPlayers;

    private static void ValidatePlayerCount(int playerCount)
    {
        if (!IsValidPlayerCount(playerCount))
            throw new ArgumentOutOfRangeException(nameof(playerCount),
                $"Player count must be between {MinPlayers} and {MaxPlayers}.");
    }
}
