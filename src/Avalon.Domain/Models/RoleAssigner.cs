using Avalon.Domain.Configuration;
using Avalon.Domain.Enums;

namespace Avalon.Domain.Models;

public class RoleAssigner
{
    private readonly Random _random;

    public RoleAssigner(Random? random = null)
    {
        _random = random ?? new Random();
    }

    public void AssignRoles(List<Player> players, GameSettings settings)
    {
        int playerCount = players.Count;
        var (goodCount, evilCount) = GameConfiguration.GetTeamComposition(playerCount);
        var (goodSpecial, evilSpecial) = settings.CountSpecialRoles();

        if (goodSpecial > goodCount)
            throw new InvalidOperationException($"Too many good special roles ({goodSpecial}) for {goodCount} good players.");
        if (evilSpecial > evilCount)
            throw new InvalidOperationException($"Too many evil special roles ({evilSpecial}) for {evilCount} evil players.");

        var roles = BuildRoleList(goodCount, evilCount, settings);

        double bias = Math.Clamp(settings.BotEvilBias, 0.0, 1.0);
        bool anyBots = players.Any(p => p.IsBot);

        if (bias <= 0.0 || !anyBots)
        {
            // Unbiased: fully random assignment (original behavior).
            var shuffledRoles = roles.OrderBy(_ => _random.Next()).ToList();
            var shuffledPlayers = players.OrderBy(_ => _random.Next()).ToList();
            for (int i = 0; i < shuffledPlayers.Count; i++)
            {
                var (role, team) = shuffledRoles[i];
                shuffledPlayers[i].Role = role;
                shuffledPlayers[i].Team = team;
            }
            return;
        }

        AssignWithBotEvilBias(players, roles, bias);
    }

    /// <summary>
    /// Steers bot players toward Evil minion roles with the given probability,
    /// and never assigns Merlin to a bot (unless every player is a bot and no
    /// non-Merlin role remains). Remaining roles go to humans randomly.
    /// </summary>
    private void AssignWithBotEvilBias(List<Player> players, List<(Role Role, Team Team)> roles, double bias)
    {
        var evilRoles = roles.Where(r => r.Team == Team.Evil).OrderBy(_ => _random.Next()).ToList();
        var goodRoles = roles.Where(r => r.Team == Team.Good).OrderBy(_ => _random.Next()).ToList();

        var bots = players.Where(p => p.IsBot).OrderBy(_ => _random.Next()).ToList();
        var humans = players.Where(p => !p.IsBot).OrderBy(_ => _random.Next()).ToList();

        foreach (var bot in bots)
        {
            bool wantEvil = evilRoles.Count > 0 && _random.NextDouble() < bias;

            if (wantEvil)
            {
                Assign(bot, evilRoles, 0);
                continue;
            }

            // Otherwise assign a NON-Merlin good role if one is available.
            int nonMerlinIdx = goodRoles.FindIndex(r => r.Role != Role.Merlin);
            if (nonMerlinIdx >= 0)
                Assign(bot, goodRoles, nonMerlinIdx);
            else if (evilRoles.Count > 0)
                Assign(bot, evilRoles, 0);
            else
                Assign(bot, goodRoles, 0); // only Merlin left (all-bot game)
        }

        // Whatever is left goes to the humans, randomly.
        var remaining = evilRoles.Concat(goodRoles).OrderBy(_ => _random.Next()).ToList();
        for (int i = 0; i < humans.Count; i++)
        {
            humans[i].Role = remaining[i].Role;
            humans[i].Team = remaining[i].Team;
        }
    }

    private static void Assign(Player player, List<(Role Role, Team Team)> pool, int index)
    {
        var (role, team) = pool[index];
        pool.RemoveAt(index);
        player.Role = role;
        player.Team = team;
    }

    private static List<(Role Role, Team Team)> BuildRoleList(int goodCount, int evilCount, GameSettings settings)
    {
        var roles = new List<(Role, Team)>();

        // Add good special roles
        if (settings.MerlinEnabled) roles.Add((Role.Merlin, Team.Good));
        if (settings.PercivalEnabled) roles.Add((Role.Percival, Team.Good));

        // Fill remaining good slots with LoyalServant
        int goodRemaining = goodCount - roles.Count;
        for (int i = 0; i < goodRemaining; i++)
            roles.Add((Role.LoyalServant, Team.Good));

        // Add evil special roles
        var evilRoles = new List<(Role, Team)>();
        if (settings.AssassinEnabled) evilRoles.Add((Role.Assassin, Team.Evil));
        if (settings.MorganaEnabled) evilRoles.Add((Role.Morgana, Team.Evil));
        if (settings.MordredEnabled) evilRoles.Add((Role.Mordred, Team.Evil));
        if (settings.OberonEnabled) evilRoles.Add((Role.Oberon, Team.Evil));

        roles.AddRange(evilRoles);

        // Fill remaining evil slots with generic evil (Minion of Mordred = Assassin without ability, but we use LoyalServant equivalent)
        // In Avalon, base evil is "Minion of Mordred" but we'll use Assassin for the required assassin role
        // Actually, if no special evil roles, fill with generic evil
        int evilRemaining = evilCount - evilRoles.Count;
        for (int i = 0; i < evilRemaining; i++)
            roles.Add((Role.Assassin, Team.Evil));

        return roles;
    }
}
