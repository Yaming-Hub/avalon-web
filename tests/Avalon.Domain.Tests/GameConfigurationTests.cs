using Avalon.Domain.Configuration;
using Avalon.Domain.Enums;
using Avalon.Domain.Models;

namespace Avalon.Domain.Tests;

[TestClass]
public class GameConfigurationTests
{
    #region Constants

    [TestMethod]
    public void Constants_HaveExpectedValues()
    {
        Assert.AreEqual(5, GameConfiguration.MinPlayers);
        Assert.AreEqual(10, GameConfiguration.MaxPlayers);
        Assert.AreEqual(5, GameConfiguration.TotalQuests);
        Assert.AreEqual(3, GameConfiguration.QuestsToWin);
        Assert.AreEqual(5, GameConfiguration.MaxConsecutiveRejections);
    }

    #endregion

    #region GetQuestTeamSize

    [TestMethod]
    [DataRow(5, 1, 2)]
    [DataRow(5, 2, 3)]
    [DataRow(5, 3, 2)]
    [DataRow(5, 4, 3)]
    [DataRow(5, 5, 3)]
    [DataRow(6, 1, 2)]
    [DataRow(6, 2, 3)]
    [DataRow(6, 3, 4)]
    [DataRow(6, 4, 3)]
    [DataRow(6, 5, 4)]
    [DataRow(7, 1, 2)]
    [DataRow(7, 2, 3)]
    [DataRow(7, 3, 3)]
    [DataRow(7, 4, 4)]
    [DataRow(7, 5, 4)]
    [DataRow(8, 1, 3)]
    [DataRow(8, 2, 4)]
    [DataRow(8, 3, 4)]
    [DataRow(8, 4, 5)]
    [DataRow(8, 5, 5)]
    [DataRow(9, 1, 3)]
    [DataRow(9, 2, 4)]
    [DataRow(9, 3, 4)]
    [DataRow(9, 4, 5)]
    [DataRow(9, 5, 5)]
    [DataRow(10, 1, 3)]
    [DataRow(10, 2, 4)]
    [DataRow(10, 3, 4)]
    [DataRow(10, 4, 5)]
    [DataRow(10, 5, 5)]
    public void GetQuestTeamSize_ReturnsCorrectSize(int playerCount, int questNumber, int expectedSize)
    {
        var result = GameConfiguration.GetQuestTeamSize(playerCount, questNumber);
        Assert.AreEqual(expectedSize, result);
    }

    [TestMethod]
    public void GetQuestTeamSize_InvalidPlayerCount_ThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => GameConfiguration.GetQuestTeamSize(4, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => GameConfiguration.GetQuestTeamSize(11, 1));
    }

    [TestMethod]
    public void GetQuestTeamSize_InvalidQuestNumber_ThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => GameConfiguration.GetQuestTeamSize(5, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => GameConfiguration.GetQuestTeamSize(5, 6));
    }

    #endregion

    #region GetAllQuestTeamSizes

    [TestMethod]
    [DataRow(5, new[] { 2, 3, 2, 3, 3 })]
    [DataRow(6, new[] { 2, 3, 4, 3, 4 })]
    [DataRow(7, new[] { 2, 3, 3, 4, 4 })]
    [DataRow(8, new[] { 3, 4, 4, 5, 5 })]
    [DataRow(9, new[] { 3, 4, 4, 5, 5 })]
    [DataRow(10, new[] { 3, 4, 4, 5, 5 })]
    public void GetAllQuestTeamSizes_ReturnsCorrectArrayForPlayerCount(int playerCount, int[] expectedSizes)
    {
        var result = GameConfiguration.GetAllQuestTeamSizes(playerCount);
        CollectionAssert.AreEqual(expectedSizes, result);
    }

    [TestMethod]
    public void GetAllQuestTeamSizes_ReturnsClonedArray()
    {
        var first = GameConfiguration.GetAllQuestTeamSizes(5);
        var second = GameConfiguration.GetAllQuestTeamSizes(5);
        Assert.AreNotSame(first, second);
    }

    [TestMethod]
    public void GetAllQuestTeamSizes_InvalidPlayerCount_ThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => GameConfiguration.GetAllQuestTeamSizes(3));
    }

    #endregion

    #region GetTeamComposition

    [TestMethod]
    [DataRow(5, 3, 2)]
    [DataRow(6, 4, 2)]
    [DataRow(7, 4, 3)]
    [DataRow(8, 5, 3)]
    [DataRow(9, 6, 3)]
    [DataRow(10, 6, 4)]
    public void GetTeamComposition_ReturnsCorrectGoodAndEvilCounts(int playerCount, int expectedGood, int expectedEvil)
    {
        var (good, evil) = GameConfiguration.GetTeamComposition(playerCount);
        Assert.AreEqual(expectedGood, good);
        Assert.AreEqual(expectedEvil, evil);
    }

    [TestMethod]
    public void GetTeamComposition_GoodPlusEvilEqualsPlayerCount()
    {
        for (int playerCount = GameConfiguration.MinPlayers; playerCount <= GameConfiguration.MaxPlayers; playerCount++)
        {
            var (good, evil) = GameConfiguration.GetTeamComposition(playerCount);
            Assert.AreEqual(playerCount, good + evil,
                $"Good ({good}) + Evil ({evil}) should equal player count ({playerCount}).");
        }
    }

    [TestMethod]
    public void GetTeamComposition_InvalidPlayerCount_ThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => GameConfiguration.GetTeamComposition(4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => GameConfiguration.GetTeamComposition(11));
    }

    #endregion

    #region GetFailsRequired

    [TestMethod]
    [DataRow(7, 4, 2)]
    [DataRow(8, 4, 2)]
    [DataRow(9, 4, 2)]
    [DataRow(10, 4, 2)]
    public void GetFailsRequired_Quest4With7PlusPlayers_Returns2(int playerCount, int questNumber, int expected)
    {
        Assert.AreEqual(expected, GameConfiguration.GetFailsRequired(playerCount, questNumber));
    }

    [TestMethod]
    [DataRow(5, 1)]
    [DataRow(5, 2)]
    [DataRow(5, 3)]
    [DataRow(5, 4)]
    [DataRow(5, 5)]
    [DataRow(6, 4)]
    [DataRow(7, 1)]
    [DataRow(7, 2)]
    [DataRow(7, 3)]
    [DataRow(7, 5)]
    [DataRow(10, 1)]
    [DataRow(10, 5)]
    public void GetFailsRequired_NonSpecialCases_Returns1(int playerCount, int questNumber)
    {
        Assert.AreEqual(1, GameConfiguration.GetFailsRequired(playerCount, questNumber));
    }

    #endregion

    #region IsValidPlayerCount

    [TestMethod]
    [DataRow(5, true)]
    [DataRow(6, true)]
    [DataRow(7, true)]
    [DataRow(8, true)]
    [DataRow(9, true)]
    [DataRow(10, true)]
    [DataRow(4, false)]
    [DataRow(11, false)]
    [DataRow(0, false)]
    [DataRow(-1, false)]
    [DataRow(100, false)]
    public void IsValidPlayerCount_ReturnsExpectedResult(int playerCount, bool expected)
    {
        Assert.AreEqual(expected, GameConfiguration.IsValidPlayerCount(playerCount));
    }

    #endregion
}

[TestClass]
public class RoleAssignerTests
{
    private static List<Player> CreatePlayers(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new Player($"p{i}", $"Player{i}", i == 1))
            .ToList();
    }

    #region Team composition correctness

    [TestMethod]
    [DataRow(5, 3, 2)]
    [DataRow(6, 4, 2)]
    [DataRow(7, 4, 3)]
    [DataRow(8, 5, 3)]
    [DataRow(9, 6, 3)]
    [DataRow(10, 6, 4)]
    public void AssignRoles_AssignsCorrectGoodAndEvilCounts(int playerCount, int expectedGood, int expectedEvil)
    {
        var assigner = new RoleAssigner(new Random(42));
        var players = CreatePlayers(playerCount);
        var settings = new GameSettings();

        assigner.AssignRoles(players, settings);

        int goodCount = players.Count(p => p.Team == Team.Good);
        int evilCount = players.Count(p => p.Team == Team.Evil);
        Assert.AreEqual(expectedGood, goodCount, $"Expected {expectedGood} good players for {playerCount} total.");
        Assert.AreEqual(expectedEvil, evilCount, $"Expected {expectedEvil} evil players for {playerCount} total.");
    }

    #endregion

    #region All players get Role and Team

    [TestMethod]
    public void AssignRoles_AllPlayersHaveNonNullRoleAndTeam()
    {
        var assigner = new RoleAssigner(new Random(42));
        var players = CreatePlayers(7);
        var settings = new GameSettings();

        assigner.AssignRoles(players, settings);

        foreach (var player in players)
        {
            Assert.IsNotNull(player.Role, $"Player {player.Name} should have a role assigned.");
            Assert.IsNotNull(player.Team, $"Player {player.Name} should have a team assigned.");
        }
    }

    [TestMethod]
    public void AssignRoles_AllPlayersHaveRoleAndTeam_ForEveryPlayerCount()
    {
        for (int count = 5; count <= 10; count++)
        {
            var assigner = new RoleAssigner(new Random(42));
            var players = CreatePlayers(count);
            var settings = new GameSettings();

            assigner.AssignRoles(players, settings);

            foreach (var player in players)
            {
                Assert.IsNotNull(player.Role, $"Player {player.Name} in {count}-player game should have a role.");
                Assert.IsNotNull(player.Team, $"Player {player.Name} in {count}-player game should have a team.");
            }
        }
    }

    #endregion

    #region Merlin enabled/disabled

    [TestMethod]
    public void AssignRoles_MerlinEnabled_AssignsMerlinToOneGoodPlayer()
    {
        var assigner = new RoleAssigner(new Random(42));
        var players = CreatePlayers(5);
        var settings = new GameSettings { MerlinEnabled = true };

        assigner.AssignRoles(players, settings);

        var merlins = players.Where(p => p.Role == Role.Merlin).ToList();
        Assert.AreEqual(1, merlins.Count, "Exactly one player should be Merlin.");
        Assert.AreEqual(Team.Good, merlins[0].Team, "Merlin should be on the Good team.");
    }

    [TestMethod]
    public void AssignRoles_MerlinDisabled_NoMerlinAssigned()
    {
        var assigner = new RoleAssigner(new Random(42));
        var players = CreatePlayers(5);
        var settings = new GameSettings { MerlinEnabled = false, AssassinEnabled = false };

        assigner.AssignRoles(players, settings);

        Assert.IsFalse(players.Any(p => p.Role == Role.Merlin), "No player should be Merlin when disabled.");
    }

    #endregion

    #region All optional roles enabled

    [TestMethod]
    public void AssignRoles_AllSpecialRolesEnabled_AssignsAllSpecialRoles()
    {
        // 10 players: 6 good, 4 evil — enough room for all special roles
        var assigner = new RoleAssigner(new Random(42));
        var players = CreatePlayers(10);
        var settings = new GameSettings
        {
            MerlinEnabled = true,
            AssassinEnabled = true,
            PercivalEnabled = true,
            MorganaEnabled = true,
            MordredEnabled = true,
            OberonEnabled = true
        };

        assigner.AssignRoles(players, settings);

        Assert.AreEqual(1, players.Count(p => p.Role == Role.Merlin), "Should have one Merlin.");
        Assert.AreEqual(1, players.Count(p => p.Role == Role.Percival), "Should have one Percival.");
        Assert.AreEqual(1, players.Count(p => p.Role == Role.Assassin), "Should have at least one Assassin.");
        Assert.AreEqual(1, players.Count(p => p.Role == Role.Morgana), "Should have one Morgana.");
        Assert.AreEqual(1, players.Count(p => p.Role == Role.Mordred), "Should have one Mordred.");
        Assert.AreEqual(1, players.Count(p => p.Role == Role.Oberon), "Should have one Oberon.");

        // Merlin and Percival are good
        Assert.AreEqual(Team.Good, players.First(p => p.Role == Role.Merlin).Team);
        Assert.AreEqual(Team.Good, players.First(p => p.Role == Role.Percival).Team);

        // Evil special roles are evil
        Assert.AreEqual(Team.Evil, players.First(p => p.Role == Role.Morgana).Team);
        Assert.AreEqual(Team.Evil, players.First(p => p.Role == Role.Mordred).Team);
        Assert.AreEqual(Team.Evil, players.First(p => p.Role == Role.Oberon).Team);
    }

    #endregion

    #region Too many special roles

    [TestMethod]
    public void AssignRoles_TooManyEvilSpecialRoles_ThrowsInvalidOperationException()
    {
        // 5 players: 3 good, 2 evil — but 4 evil special roles requested
        var assigner = new RoleAssigner(new Random(42));
        var players = CreatePlayers(5);
        var settings = new GameSettings
        {
            MerlinEnabled = false,
            AssassinEnabled = true,
            MorganaEnabled = true,
            MordredEnabled = true,
            OberonEnabled = true
        };

        Assert.ThrowsExactly<InvalidOperationException>(
            () => assigner.AssignRoles(players, settings));
    }

    [TestMethod]
    public void AssignRoles_TooManyEvilSpecialRolesFor6Players_ThrowsInvalidOperationException()
    {
        // 6 players: 4 good, 2 evil — enabling all 4 evil specials should throw.
        var assigner = new RoleAssigner(new Random(42));
        var players = CreatePlayers(6);
        var settings = new GameSettings
        {
            MerlinEnabled = false,
            AssassinEnabled = true,
            MorganaEnabled = true,
            MordredEnabled = true,
            OberonEnabled = true
        };

        Assert.ThrowsExactly<InvalidOperationException>(
            () => assigner.AssignRoles(players, settings));
    }

    #endregion

    #region Deterministic with seeded Random

    [TestMethod]
    public void AssignRoles_WithSameRandomSeed_ProducesSameAssignment()
    {
        var players1 = CreatePlayers(7);
        var players2 = CreatePlayers(7);
        var settings = new GameSettings { MerlinEnabled = true, AssassinEnabled = true, PercivalEnabled = true };

        new RoleAssigner(new Random(123)).AssignRoles(players1, settings);
        new RoleAssigner(new Random(123)).AssignRoles(players2, settings);

        for (int i = 0; i < players1.Count; i++)
        {
            Assert.AreEqual(players1[i].Role, players2[i].Role,
                $"Player index {i} role mismatch on deterministic run.");
            Assert.AreEqual(players1[i].Team, players2[i].Team,
                $"Player index {i} team mismatch on deterministic run.");
        }
    }

    #endregion

    #region Default settings

    [TestMethod]
    public void AssignRoles_DefaultSettings_HasMerlinAndAssassin()
    {
        var assigner = new RoleAssigner(new Random(42));
        var players = CreatePlayers(5);
        var settings = new GameSettings(); // MerlinEnabled=true, AssassinEnabled=true by default

        assigner.AssignRoles(players, settings);

        Assert.IsTrue(players.Any(p => p.Role == Role.Merlin), "Default settings should assign Merlin.");
        Assert.IsTrue(players.Any(p => p.Role == Role.Assassin), "Default settings should assign Assassin.");
    }

    #endregion

    #region Remaining players are LoyalServant / Assassin (generic evil)

    [TestMethod]
    public void AssignRoles_DefaultSettings_RemainingGoodAreLoyalServants()
    {
        var assigner = new RoleAssigner(new Random(42));
        var players = CreatePlayers(5);
        var settings = new GameSettings(); // Merlin + Assassin only

        assigner.AssignRoles(players, settings);

        var goodPlayers = players.Where(p => p.Team == Team.Good).ToList();
        int loyalServants = goodPlayers.Count(p => p.Role == Role.LoyalServant);
        int merlins = goodPlayers.Count(p => p.Role == Role.Merlin);

        Assert.AreEqual(1, merlins);
        Assert.AreEqual(2, loyalServants, "Non-Merlin good players should be LoyalServants.");
    }

    #endregion
}
