namespace Avalon.Application.Services;

/// <summary>
/// Defines a bot's personality which influences decision-making.
/// Each bot gets a personality assigned at creation, creating diverse behavior.
/// </summary>
public class BotPersonality
{
    public string Name { get; init; } = default!;
    public string Description { get; init; } = default!;

    // How likely to reject a team proposal (0.0 = always approve, 1.0 = always reject)
    public double Aggressiveness { get; init; }

    // How much suspicion score influences decisions (0.0 = ignores data, 1.0 = strictly follows)
    public double Analytical { get; init; }

    // As evil, probability of voting Success on a quest to avoid detection
    public double Deceptiveness { get; init; }

    // How likely to deviate from "optimal" play for unpredictability
    public double Randomness { get; init; }

    // Evil-specific: how likely to reject clean teams (risk of being caught)
    public double Boldness { get; init; }

    // Predefined personalities
    public static readonly BotPersonality Cautious = new()
    {
        Name = "Cautious",
        Description = "Plays it safe, rarely takes risks",
        Aggressiveness = 0.2,
        Analytical = 0.9,
        Deceptiveness = 0.5,
        Randomness = 0.1,
        Boldness = 0.2,
    };

    public static readonly BotPersonality Aggressive = new()
    {
        Name = "Aggressive",
        Description = "Quick to reject, bold decisions",
        Aggressiveness = 0.7,
        Analytical = 0.5,
        Deceptiveness = 0.2,
        Randomness = 0.2,
        Boldness = 0.8,
    };

    public static readonly BotPersonality Mastermind = new()
    {
        Name = "Mastermind",
        Description = "Highly analytical, deceptive when evil",
        Aggressiveness = 0.4,
        Analytical = 1.0,
        Deceptiveness = 0.7,
        Randomness = 0.05,
        Boldness = 0.5,
    };

    public static readonly BotPersonality Chaotic = new()
    {
        Name = "Chaotic",
        Description = "Unpredictable, hard to read",
        Aggressiveness = 0.5,
        Analytical = 0.3,
        Deceptiveness = 0.4,
        Randomness = 0.6,
        Boldness = 0.6,
    };

    public static readonly BotPersonality Loyal = new()
    {
        Name = "Loyal",
        Description = "Trusting, approves often, subtle when evil",
        Aggressiveness = 0.1,
        Analytical = 0.6,
        Deceptiveness = 0.8,
        Randomness = 0.15,
        Boldness = 0.3,
    };

    public static readonly BotPersonality[] All = { Cautious, Aggressive, Mastermind, Chaotic, Loyal };

    /// <summary>
    /// Get personality for a bot by name. Assigns deterministically based on bot name hash.
    /// </summary>
    public static BotPersonality GetForBot(string botName)
    {
        int hash = Math.Abs(botName.GetHashCode());
        return All[hash % All.Length];
    }
}
