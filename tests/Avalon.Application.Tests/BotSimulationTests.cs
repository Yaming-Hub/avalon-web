using Avalon.Application.Interfaces;
using Avalon.Application.Services;
using Avalon.Domain.Enums;
using Avalon.Domain.Models;
using Avalon.Infrastructure.Persistence;

namespace Avalon.Application.Tests;

[TestClass]
public class BotSimulationTests
{
    public TestContext TestContext { get; set; } = default!;

    /// <summary>
    /// No-op notifier so BotService's await calls don't NRE.
    /// </summary>
    private sealed class NullNotifier : IGameNotifier
    {
        public Task NotifyPlayerJoined(string gameId, string playerName) => Task.CompletedTask;
        public Task NotifyPlayerLeft(string gameId, string playerName) => Task.CompletedTask;
        public Task NotifyGameStarted(string gameId) => Task.CompletedTask;
        public Task NotifyPhaseChanged(string gameId, string phase) => Task.CompletedTask;
        public Task NotifyTeamProposed(string gameId, string leaderName, List<string> proposedPlayerNames) => Task.CompletedTask;
        public Task NotifyVoteRevealed(string gameId, Dictionary<string, string> votes) => Task.CompletedTask;
        public Task NotifyQuestResult(string gameId, int successes, int fails) => Task.CompletedTask;
        public Task NotifyLadyResult(string connectionId, string targetAlignment) => Task.CompletedTask;
        public Task NotifyGameOver(string gameId, string result) => Task.CompletedTask;
    }

    private sealed class GameOutcome
    {
        public GameResult Result;
        public int GoodQuests;
        public int EvilQuests;
        public int Rounds;
        public bool ReachedAssassination;
        public bool AssassinHitMerlin;
    }

    /// <summary>
    /// Runs a single all-bot game to completion and returns the outcome.
    /// </summary>
    private static async Task<GameOutcome> RunBotGameAsync(int playerCount, BotIntelligence sharedIntelligence)
    {
        var repo = new InMemoryGameRepository();
        var notifier = new NullNotifier();
        var log = new InMemoryActivityLog();
        var chat = new InMemoryConversationService();
        var botService = new BotService(repo, notifier, log, chat, sharedIntelligence);

        var gameId = Guid.NewGuid().ToString("N")[..8];
        var game = new Game(gameId, new RoleAssigner());

        // All players are bots; the first one is also the host.
        game.Players.Add(new Player(Guid.NewGuid().ToString(), "Bot_Host", isHost: true, isBot: true));
        for (int i = 1; i < playerCount; i++)
            game.Players.Add(new Player(Guid.NewGuid().ToString(), $"Bot_{i}", isHost: false, isBot: true));

        var hostId = game.Players[0].Id;
        game.Start(hostId);
        game.ProceedFromRoleReveal();
        await repo.SaveAsync(game);

        // Drive the game with the real bot logic until it ends.
        int safety = 0;
        while (game.Phase != GamePhase.GameOver && safety++ < 1000)
        {
            await botService.ProcessBotActionsAsync(gameId);
            game = (await repo.GetByIdAsync(gameId))!;
        }

        if (game.Phase != GamePhase.GameOver || game.Result is null)
            throw new InvalidOperationException($"Game did not finish (phase={game.Phase}, safety={safety}).");

        var merlin = game.Players.FirstOrDefault(p => p.Role == Role.Merlin);
        return new GameOutcome
        {
            Result = game.Result.Value,
            GoodQuests = game.CompletedQuestsGood,
            EvilQuests = game.CompletedQuestsEvil,
            Rounds = game.Rounds.Count,
            ReachedAssassination = game.AssassinTargetId != null || game.Result == GameResult.EvilWinsByAssassination,
            AssassinHitMerlin = merlin != null && game.AssassinTargetId == merlin.Id,
        };
    }

    [TestMethod]
    [DataRow(5, 100)]
    [DataRow(7, 100)]
    public async Task BotGames_WinRateDistribution(int playerCount, int iterations)
    {
        var sharedIntelligence = new BotIntelligence(); // shared so cross-game learning accumulates
        int goodWins = 0, evilWinsQuests = 0, evilWinsAssassination = 0;
        int reachedAssassination = 0, assassinHitMerlin = 0;
        int totalRounds = 0;

        for (int i = 0; i < iterations; i++)
        {
            var outcome = await RunBotGameAsync(playerCount, sharedIntelligence);
            totalRounds += outcome.Rounds;

            switch (outcome.Result)
            {
                case GameResult.GoodWins: goodWins++; break;
                case GameResult.EvilWins: evilWinsQuests++; break;
                case GameResult.EvilWinsByAssassination: evilWinsAssassination++; break;
            }
            if (outcome.ReachedAssassination) reachedAssassination++;
            if (outcome.AssassinHitMerlin) assassinHitMerlin++;
        }

        int totalEvil = evilWinsQuests + evilWinsAssassination;
        double goodPct = 100.0 * goodWins / iterations;
        double evilPct = 100.0 * totalEvil / iterations;

        TestContext.WriteLine($"==== {playerCount}-bot games x{iterations} ====");
        TestContext.WriteLine($"GOOD wins : {goodWins} ({goodPct:F1}%)");
        TestContext.WriteLine($"EVIL wins : {totalEvil} ({evilPct:F1}%)");
        TestContext.WriteLine($"   - by 3 failed quests        : {evilWinsQuests}");
        TestContext.WriteLine($"   - by assassinating Merlin   : {evilWinsAssassination}");
        TestContext.WriteLine($"Reached assassination phase    : {reachedAssassination}");
        TestContext.WriteLine($"  Assassin guessed Merlin right: {assassinHitMerlin}" +
            (reachedAssassination > 0 ? $" ({100.0 * assassinHitMerlin / reachedAssassination:F1}% of assassinations)" : ""));
        TestContext.WriteLine($"Avg rounds per game            : {(double)totalRounds / iterations:F2}");
        TestContext.WriteLine($"==========================================");

        // Sanity: every game finished and both outcomes are possible.
        Assert.AreEqual(iterations, goodWins + totalEvil, "All games should resolve to a win.");
        Assert.IsTrue(goodWins > 0, "Good should win at least once.");
        Assert.IsTrue(totalEvil > 0, "Evil should win at least once.");
    }
}
