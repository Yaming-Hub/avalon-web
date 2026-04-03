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
        var shuffledRoles = roles.OrderBy(_ => _random.Next()).ToList();

        var shuffledPlayers = players.OrderBy(_ => _random.Next()).ToList();

        for (int i = 0; i < shuffledPlayers.Count; i++)
        {
            var (role, team) = shuffledRoles[i];
            shuffledPlayers[i].Role = role;
            shuffledPlayers[i].Team = team;
        }
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
