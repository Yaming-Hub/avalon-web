using Avalon.Domain.Enums;
using Avalon.Domain.Models;

namespace Avalon.Domain.Tests;

[TestClass]
public class BotEvilBiasTests
{
    private static List<Player> MakePlayers(int humans, int bots)
    {
        var list = new List<Player>();
        for (int i = 0; i < humans; i++)
            list.Add(new Player(Guid.NewGuid().ToString(), $"Human{i}", isHost: i == 0, isBot: false));
        for (int i = 0; i < bots; i++)
            list.Add(new Player(Guid.NewGuid().ToString(), $"Bot{i}", isHost: false, isBot: true));
        return list;
    }

    [TestMethod]
    public void BiasZero_BotCanBeAnyRole_BehaviorUnchanged()
    {
        // With bias 0, assignment is fully random — over many runs a bot
        // should sometimes be good and sometimes evil.
        int botGood = 0, botEvil = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            var assigner = new RoleAssigner(new Random(seed));
            var players = MakePlayers(humans: 6, bots: 1);
            assigner.AssignRoles(players, new GameSettings { BotEvilBias = 0.0 });
            var bot = players.First(p => p.IsBot);
            if (bot.Team == Team.Good) botGood++; else botEvil++;
        }

        Assert.IsTrue(botGood > 0, "Bot should sometimes be good when unbiased.");
        Assert.IsTrue(botEvil > 0, "Bot should sometimes be evil when unbiased.");
    }

    [TestMethod]
    public void BiasOne_SingleBot_AlwaysEvil_NeverMerlin()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var assigner = new RoleAssigner(new Random(seed));
            var players = MakePlayers(humans: 6, bots: 1);
            assigner.AssignRoles(players, new GameSettings { BotEvilBias = 1.0 });
            var bot = players.First(p => p.IsBot);

            Assert.AreEqual(Team.Evil, bot.Team, $"With bias 1.0 the bot should always be evil (seed {seed}).");
            Assert.AreNotEqual(Role.Merlin, bot.Role, "A biased bot must never be Merlin.");
        }
    }

    [TestMethod]
    public void BiasOne_AlwaysProducesValidComposition()
    {
        // Even with bias, the overall team composition must stay correct
        // (4 good / 3 evil for 7 players) and all special roles present once.
        var assigner = new RoleAssigner(new Random(42));
        var players = MakePlayers(humans: 5, bots: 2);
        var settings = new GameSettings { BotEvilBias = 1.0 };
        assigner.AssignRoles(players, settings);

        Assert.AreEqual(4, players.Count(p => p.Team == Team.Good));
        Assert.AreEqual(3, players.Count(p => p.Team == Team.Evil));
        Assert.AreEqual(1, players.Count(p => p.Role == Role.Merlin));
        // Merlin must be a human.
        Assert.IsFalse(players.First(p => p.Role == Role.Merlin).IsBot);
    }

    [TestMethod]
    public void Bias_ReducesBotGoodRateVersusUnbiased()
    {
        double Rate(double bias)
        {
            int botGood = 0;
            for (int seed = 0; seed < 300; seed++)
            {
                var assigner = new RoleAssigner(new Random(seed));
                var players = MakePlayers(humans: 6, bots: 1);
                assigner.AssignRoles(players, new GameSettings { BotEvilBias = bias });
                if (players.First(p => p.IsBot).Team == Team.Good) botGood++;
            }
            return botGood / 300.0;
        }

        double unbiased = Rate(0.0);
        double biased = Rate(0.5);

        Assert.IsTrue(biased < unbiased,
            $"Bias 0.5 should lower the bot's good-rate (unbiased={unbiased:F2}, biased={biased:F2}).");
    }
}
