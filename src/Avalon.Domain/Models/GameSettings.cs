using Avalon.Domain.Enums;

namespace Avalon.Domain.Models;

public class GameSettings
{
    public bool MerlinEnabled { get; set; } = true;
    public bool AssassinEnabled { get; set; } = true;
    public bool PercivalEnabled { get; set; }
    public bool MorganaEnabled { get; set; }
    public bool MordredEnabled { get; set; }
    public bool OberonEnabled { get; set; }
    public bool LadyOfTheLakeEnabled { get; set; }

    public List<Role> GetEnabledSpecialRoles()
    {
        var roles = new List<Role>();
        if (MerlinEnabled) roles.Add(Enums.Role.Merlin);
        if (AssassinEnabled) roles.Add(Enums.Role.Assassin);
        if (PercivalEnabled) roles.Add(Enums.Role.Percival);
        if (MorganaEnabled) roles.Add(Enums.Role.Morgana);
        if (MordredEnabled) roles.Add(Enums.Role.Mordred);
        if (OberonEnabled) roles.Add(Enums.Role.Oberon);
        return roles;
    }

    public (int GoodSpecial, int EvilSpecial) CountSpecialRoles()
    {
        int good = 0, evil = 0;
        if (MerlinEnabled) good++;
        if (PercivalEnabled) good++;
        if (AssassinEnabled) evil++;
        if (MorganaEnabled) evil++;
        if (MordredEnabled) evil++;
        if (OberonEnabled) evil++;
        return (good, evil);
    }
}
