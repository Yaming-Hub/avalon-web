using Avalon.Domain.Configuration;
using Avalon.Domain.Enums;
using Avalon.Domain.Models;

namespace Avalon.Domain.Tests;

[TestClass]
public class GameScenarioTests
{
    #region Helpers

    private static Game CreateStartedGame(int playerCount, GameSettings? settings = null, int seed = 42)
    {
        var game = new Game("test", new RoleAssigner(new Random(seed)));
        for (int i = 0; i < playerCount; i++)
            game.Join($"Player{i}");
        if (settings != null)
            game.UpdateSettings(game.Players[0].Id, settings);
        game.Start(game.Players[0].Id);
        game.ProceedFromRoleReveal();
        return game;
    }

    private static List<string> BuildTeamWithEvil(Game game, int evilCount = 1)
    {
        var teamSize = game.CurrentRound!.RequiredTeamSize;
        var evilPlayers = game.Players.Where(p => p.Team == Team.Evil).ToList();
        var goodPlayers = game.Players.Where(p => p.Team == Team.Good).ToList();

        var team = new List<Player>();
        team.AddRange(evilPlayers.Take(evilCount));
        team.AddRange(goodPlayers.Take(teamSize - team.Count));
        if (team.Count < teamSize)
            team.AddRange(evilPlayers.Skip(evilCount).Take(teamSize - team.Count));

        return team.Select(p => p.Id).ToList();
    }

    private static void ProposeAndApproveTeam(Game game, List<string> teamPlayerIds)
    {
        game.ProposeTeam(game.CurrentLeader!.Id, teamPlayerIds);
        foreach (var player in game.Players)
            game.VoteOnProposal(player.Id, VoteType.Approve);
    }

    private static void RunSuccessfulQuest(Game game)
    {
        var teamSize = game.CurrentRound!.RequiredTeamSize;
        var teamPlayerIds = game.Players.Take(teamSize).Select(p => p.Id).ToList();
        ProposeAndApproveTeam(game, teamPlayerIds);
        // All members vote Success (evil players are allowed to vote Success)
        foreach (var pid in teamPlayerIds)
            game.VoteOnQuest(pid, QuestVote.Success);
        game.ProceedFromQuestResult();
    }

    private static void RunFailedQuest(Game game)
    {
        var teamPlayerIds = BuildTeamWithEvil(game);
        ProposeAndApproveTeam(game, teamPlayerIds);
        // Evil members vote Fail, good members vote Success (domain enforces good=Success)
        foreach (var pid in teamPlayerIds)
        {
            var player = game.Players.First(p => p.Id == pid);
            game.VoteOnQuest(pid, player.Team == Team.Evil ? QuestVote.Fail : QuestVote.Success);
        }
        game.ProceedFromQuestResult();
    }

    #endregion

    [TestMethod]
    public void GoodWinsFullGame()
    {
        var game = CreateStartedGame(5);

        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);
        Assert.AreEqual(1, game.CurrentRound!.RoundNumber);

        // Run 3 successful quests (all players vote Success)
        RunSuccessfulQuest(game);
        Assert.AreEqual(1, game.CompletedQuestsGood);

        RunSuccessfulQuest(game);
        Assert.AreEqual(2, game.CompletedQuestsGood);
        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);

        RunSuccessfulQuest(game);
        Assert.AreEqual(3, game.CompletedQuestsGood);

        // After 3 good wins with Merlin+Assassin enabled → AssassinVote
        Assert.AreEqual(GamePhase.AssassinVote, game.Phase);

        // Assassin targets a non-Merlin good player → Good wins
        var assassin = game.Players.First(p => p.Role == Role.Assassin);
        var nonMerlinGood = game.Players.First(p => p.Team == Team.Good && p.Role != Role.Merlin);
        game.Assassinate(assassin.Id, nonMerlinGood.Id);

        Assert.AreEqual(GamePhase.GameOver, game.Phase);
        Assert.AreEqual(GameResult.GoodWins, game.Result);
    }

    [TestMethod]
    public void EvilWinsThreeQuests()
    {
        var game = CreateStartedGame(5);

        // Run 3 failed quests (evil players vote Fail)
        RunFailedQuest(game);
        Assert.AreEqual(1, game.CompletedQuestsEvil);
        Assert.AreEqual(0, game.CompletedQuestsGood);
        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);

        RunFailedQuest(game);
        Assert.AreEqual(2, game.CompletedQuestsEvil);
        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);

        RunFailedQuest(game);
        Assert.AreEqual(3, game.CompletedQuestsEvil);

        // 3 evil wins → game over
        Assert.AreEqual(GamePhase.GameOver, game.Phase);
        Assert.AreEqual(GameResult.EvilWins, game.Result);
        Assert.AreEqual(0, game.CompletedQuestsGood);
    }

    [TestMethod]
    public void EvilWinsByAssassination()
    {
        var game = CreateStartedGame(5);

        // Run 3 successful quests
        RunSuccessfulQuest(game);
        RunSuccessfulQuest(game);
        RunSuccessfulQuest(game);

        Assert.AreEqual(GamePhase.AssassinVote, game.Phase);
        Assert.AreEqual(3, game.CompletedQuestsGood);

        // Assassin correctly identifies Merlin
        var assassin = game.Players.First(p => p.Role == Role.Assassin);
        var merlin = game.Players.First(p => p.Role == Role.Merlin);
        game.Assassinate(assassin.Id, merlin.Id);

        Assert.AreEqual(GamePhase.GameOver, game.Phase);
        Assert.AreEqual(GameResult.EvilWinsByAssassination, game.Result);
        Assert.AreEqual(merlin.Id, game.AssassinTargetId);
    }

    [TestMethod]
    public void EvilWinsByFiveRejections()
    {
        var game = CreateStartedGame(5);

        Assert.AreEqual(1, game.CurrentRound!.RoundNumber);

        // 5 consecutive proposal rejections in round 1
        for (int i = 0; i < 5; i++)
        {
            Assert.AreEqual(GamePhase.TeamProposal, game.Phase);

            var teamSize = game.CurrentRound!.RequiredTeamSize;
            var teamPlayerIds = game.Players.Take(teamSize).Select(p => p.Id).ToList();
            game.ProposeTeam(game.CurrentLeader!.Id, teamPlayerIds);

            Assert.AreEqual(GamePhase.TeamVote, game.Phase);

            // Majority rejects (all reject)
            foreach (var player in game.Players)
                game.VoteOnProposal(player.Id, VoteType.Reject);
        }

        Assert.AreEqual(GamePhase.GameOver, game.Phase);
        Assert.AreEqual(GameResult.EvilWins, game.Result);
        Assert.AreEqual(5, game.ConsecutiveRejections);
        // Still on round 1 — no quest was ever approved
        Assert.AreEqual(1, game.CurrentRound!.RoundNumber);
    }

    [TestMethod]
    public void MixedQuestResults()
    {
        var game = CreateStartedGame(5);

        // Quest 1: success
        RunSuccessfulQuest(game);
        Assert.AreEqual(1, game.CompletedQuestsGood);
        Assert.AreEqual(0, game.CompletedQuestsEvil);
        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);
        Assert.AreEqual(2, game.CurrentRound!.RoundNumber);

        // Quest 2: fail
        RunFailedQuest(game);
        Assert.AreEqual(1, game.CompletedQuestsGood);
        Assert.AreEqual(1, game.CompletedQuestsEvil);
        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);
        Assert.AreEqual(3, game.CurrentRound!.RoundNumber);

        // Quest 3: success
        RunSuccessfulQuest(game);
        Assert.AreEqual(2, game.CompletedQuestsGood);
        Assert.AreEqual(1, game.CompletedQuestsEvil);
        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);
        Assert.AreEqual(4, game.CurrentRound!.RoundNumber);

        // Quest 4: fail
        RunFailedQuest(game);
        Assert.AreEqual(2, game.CompletedQuestsGood);
        Assert.AreEqual(2, game.CompletedQuestsEvil);
        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);
        Assert.AreEqual(5, game.CurrentRound!.RoundNumber);

        // Quest 5: success → 3 good wins → AssassinVote
        RunSuccessfulQuest(game);
        Assert.AreEqual(3, game.CompletedQuestsGood);
        Assert.AreEqual(2, game.CompletedQuestsEvil);
        Assert.AreEqual(GamePhase.AssassinVote, game.Phase);

        // Verify all 5 round results
        Assert.AreEqual(5, game.Rounds.Count);
        Assert.IsTrue(game.Rounds[0].IsSuccess!.Value);
        Assert.IsFalse(game.Rounds[1].IsSuccess!.Value);
        Assert.IsTrue(game.Rounds[2].IsSuccess!.Value);
        Assert.IsFalse(game.Rounds[3].IsSuccess!.Value);
        Assert.IsTrue(game.Rounds[4].IsSuccess!.Value);
    }

    [TestMethod]
    public void LadyOfTheLakeScenario()
    {
        var settings = new GameSettings { LadyOfTheLakeEnabled = true };
        var game = CreateStartedGame(7, settings);

        Assert.IsTrue(game.IsLadyOfTheLakeActive);
        Assert.IsNotNull(game.LadyOfTheLake);
        Assert.IsNotNull(game.LadyOfTheLake.CurrentHolderId);

        // Quest 1 — Lady does NOT activate after quest 1
        RunSuccessfulQuest(game);
        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);
        Assert.AreEqual(2, game.CurrentRound!.RoundNumber);

        // Quest 2 — Lady activates after quest 2 (completedRounds >= 2)
        RunSuccessfulQuest(game);
        Assert.AreEqual(GamePhase.LadyOfTheLake, game.Phase);

        // Lady holder investigates a player
        var ladyHolderId = game.LadyOfTheLake!.CurrentHolderId!;
        var previousHolders = game.LadyOfTheLake.GetPreviousHolders();
        var target = game.Players.First(p =>
            p.Id != ladyHolderId && !previousHolders.Contains(p.Id));

        var revealedTeam = game.InvestigateWithLady(ladyHolderId, target.Id);

        // Returns correct team alignment
        Assert.AreEqual(target.Team!.Value, revealedTeam);

        // After investigation, game continues to next round
        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);
        Assert.AreEqual(3, game.CurrentRound!.RoundNumber);

        // Lady transferred to the investigated player
        Assert.AreEqual(target.Id, game.LadyOfTheLake.CurrentHolderId);
        Assert.AreEqual(1, game.LadyOfTheLake.InvestigationHistory.Count);
    }

    [TestMethod]
    public void TenPlayerGameAllRoles()
    {
        var settings = new GameSettings
        {
            MerlinEnabled = true,
            AssassinEnabled = true,
            PercivalEnabled = true,
            MorganaEnabled = true,
            MordredEnabled = true,
            OberonEnabled = true,
            LadyOfTheLakeEnabled = true
        };
        var game = CreateStartedGame(10, settings);

        // Verify team composition: 6 good, 4 evil
        var goodPlayers = game.Players.Where(p => p.Team == Team.Good).ToList();
        var evilPlayers = game.Players.Where(p => p.Team == Team.Evil).ToList();
        Assert.AreEqual(6, goodPlayers.Count);
        Assert.AreEqual(4, evilPlayers.Count);

        // Verify all special roles are assigned exactly once
        Assert.AreEqual(1, game.Players.Count(p => p.Role == Role.Merlin));
        Assert.AreEqual(1, game.Players.Count(p => p.Role == Role.Percival));
        Assert.AreEqual(1, game.Players.Count(p => p.Role == Role.Assassin));
        Assert.AreEqual(1, game.Players.Count(p => p.Role == Role.Morgana));
        Assert.AreEqual(1, game.Players.Count(p => p.Role == Role.Mordred));
        Assert.AreEqual(1, game.Players.Count(p => p.Role == Role.Oberon));
        Assert.AreEqual(4, game.Players.Count(p => p.Role == Role.LoyalServant));

        // Verify good special roles are on the Good team
        Assert.AreEqual(Team.Good, game.Players.First(p => p.Role == Role.Merlin).Team);
        Assert.AreEqual(Team.Good, game.Players.First(p => p.Role == Role.Percival).Team);

        // Verify evil special roles are on the Evil team
        Assert.AreEqual(Team.Evil, game.Players.First(p => p.Role == Role.Assassin).Team);
        Assert.AreEqual(Team.Evil, game.Players.First(p => p.Role == Role.Morgana).Team);
        Assert.AreEqual(Team.Evil, game.Players.First(p => p.Role == Role.Mordred).Team);
        Assert.AreEqual(Team.Evil, game.Players.First(p => p.Role == Role.Oberon).Team);

        // Lady of the Lake is active
        Assert.IsTrue(game.IsLadyOfTheLakeActive);

        // Play 1 quest to verify game flow works with 10 players
        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);
        Assert.AreEqual(1, game.CurrentRound!.RoundNumber);
        Assert.AreEqual(3, game.CurrentRound!.RequiredTeamSize); // 10 players, quest 1 → team of 3

        RunSuccessfulQuest(game);

        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);
        Assert.AreEqual(2, game.CurrentRound!.RoundNumber);
        Assert.AreEqual(1, game.CompletedQuestsGood);
    }

    [TestMethod]
    public void Quest4TwoFailRule()
    {
        var game = CreateStartedGame(7);

        // Verify the two-fail rule for 7+ players quest 4
        Assert.AreEqual(2, GameConfiguration.GetFailsRequired(7, 4));

        // Run quests 1–3: success, fail, fail → Good=1, Evil=2
        RunSuccessfulQuest(game);
        RunFailedQuest(game);
        RunFailedQuest(game);

        Assert.AreEqual(1, game.CompletedQuestsGood);
        Assert.AreEqual(2, game.CompletedQuestsEvil);

        // Now at quest 4: team size 4, requires 2 fails to fail
        Assert.AreEqual(4, game.CurrentRound!.RoundNumber);
        Assert.AreEqual(4, game.CurrentRound!.RequiredTeamSize);
        Assert.AreEqual(2, game.CurrentRound!.FailsRequired);

        // Build a team of 4 with exactly 1 evil player
        var evilPlayers = game.Players.Where(p => p.Team == Team.Evil).ToList();
        var goodPlayers = game.Players.Where(p => p.Team == Team.Good).ToList();
        var team = new List<string> { evilPlayers[0].Id };
        team.AddRange(goodPlayers.Take(3).Select(p => p.Id));
        Assert.AreEqual(4, team.Count);

        ProposeAndApproveTeam(game, team);

        // Evil votes Fail (1), good votes Success (3)
        foreach (var pid in team)
        {
            var player = game.Players.First(p => p.Id == pid);
            game.VoteOnQuest(pid, player.Team == Team.Evil ? QuestVote.Fail : QuestVote.Success);
        }

        // 1 fail < 2 required → quest SUCCEEDS
        Assert.AreEqual(GamePhase.QuestResult, game.Phase);
        Assert.IsTrue(game.CurrentRound!.IsSuccess!.Value);
        Assert.AreEqual(1, game.CurrentRound!.Quest!.FailCount);
        Assert.AreEqual(3, game.CurrentRound!.Quest!.SuccessCount);

        game.ProceedFromQuestResult();

        // Good=2, Evil=2 — neither has 3, game continues to quest 5
        Assert.AreEqual(2, game.CompletedQuestsGood);
        Assert.AreEqual(2, game.CompletedQuestsEvil);
        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);
        Assert.AreEqual(5, game.CurrentRound!.RoundNumber);
    }
}
