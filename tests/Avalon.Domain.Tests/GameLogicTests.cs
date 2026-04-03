using Avalon.Domain.Configuration;
using Avalon.Domain.Enums;
using Avalon.Domain.Models;

namespace Avalon.Domain.Tests;

[TestClass]
public class GameLogicTests
{
    #region Helpers

    private static Game CreateStartedGame(int playerCount = 5)
    {
        var game = new Game("test", new RoleAssigner(new Random(42)));
        var players = new List<Player>();
        for (int i = 0; i < playerCount; i++)
            players.Add(game.Join($"Player{i}"));
        game.Start(players[0].Id);
        game.ProceedFromRoleReveal();
        return game;
    }

    private static Game CreateLobbyGame(int playerCount = 5)
    {
        var game = new Game("test", new RoleAssigner(new Random(42)));
        for (int i = 0; i < playerCount; i++)
            game.Join($"Player{i}");
        return game;
    }

    private static void ApproveCurrentProposal(Game game)
    {
        foreach (var player in game.Players)
            game.VoteOnProposal(player.Id, VoteType.Approve);
    }

    private static void RejectCurrentProposal(Game game)
    {
        foreach (var player in game.Players)
            game.VoteOnProposal(player.Id, VoteType.Reject);
    }

    private static void CompleteQuestAllSuccess(Game game)
    {
        var quest = game.CurrentRound!.Quest!;
        foreach (var pid in quest.ParticipantIds)
            game.VoteOnQuest(pid, QuestVote.Success);
    }

    private static void CompleteQuestWithFail(Game game)
    {
        var quest = game.CurrentRound!.Quest!;
        foreach (var pid in quest.ParticipantIds)
        {
            var player = game.Players.First(p => p.Id == pid);
            if (player.Team == Team.Evil)
                game.VoteOnQuest(pid, QuestVote.Fail);
            else
                game.VoteOnQuest(pid, QuestVote.Success);
        }
    }

    private static Proposal ProposeCurrentTeam(Game game)
    {
        var leader = game.CurrentLeader!;
        int teamSize = game.CurrentRound!.RequiredTeamSize;
        var teamIds = game.Players.Take(teamSize).Select(p => p.Id).ToList();
        return game.ProposeTeam(leader.Id, teamIds);
    }

    /// <summary>
    /// Proposes a team that includes at least one evil player (if possible)
    /// so that quest fail votes are available.
    /// </summary>
    private static Proposal ProposeTeamWithEvil(Game game)
    {
        var leader = game.CurrentLeader!;
        int teamSize = game.CurrentRound!.RequiredTeamSize;
        var evil = game.Players.Where(p => p.Team == Team.Evil).ToList();
        var good = game.Players.Where(p => p.Team == Team.Good).ToList();
        var team = evil.Take(Math.Min(evil.Count, teamSize))
            .Concat(good.Take(Math.Max(0, teamSize - Math.Min(evil.Count, teamSize))))
            .Select(p => p.Id)
            .ToList();
        return game.ProposeTeam(leader.Id, team);
    }

    /// <summary>
    /// Runs a full round: propose → approve → quest success, then proceed.
    /// Returns true if the game is still going (not GameOver/AssassinVote).
    /// </summary>
    private static bool RunSuccessfulRound(Game game)
    {
        ProposeCurrentTeam(game);
        ApproveCurrentProposal(game);
        CompleteQuestAllSuccess(game);
        if (game.Phase == GamePhase.GameOver || game.Phase == GamePhase.AssassinVote)
            return false;
        game.ProceedFromQuestResult();
        // Skip Lady of the Lake if active
        if (game.Phase == GamePhase.LadyOfTheLake)
        {
            var lady = game.LadyOfTheLake!;
            var target = game.Players.First(p =>
                p.Id != lady.CurrentHolderId &&
                !lady.GetPreviousHolders().Contains(p.Id));
            game.InvestigateWithLady(lady.CurrentHolderId!, target.Id);
        }
        return game.Phase != GamePhase.GameOver && game.Phase != GamePhase.AssassinVote;
    }

    /// <summary>
    /// Runs a full round with quest failure (evil votes fail).
    /// </summary>
    private static bool RunFailedRound(Game game)
    {
        ProposeTeamWithEvil(game);
        ApproveCurrentProposal(game);
        CompleteQuestWithFail(game);
        if (game.Phase == GamePhase.GameOver || game.Phase == GamePhase.AssassinVote)
            return false;
        game.ProceedFromQuestResult();
        if (game.Phase == GamePhase.LadyOfTheLake)
        {
            var lady = game.LadyOfTheLake!;
            var target = game.Players.First(p =>
                p.Id != lady.CurrentHolderId &&
                !lady.GetPreviousHolders().Contains(p.Id));
            game.InvestigateWithLady(lady.CurrentHolderId!, target.Id);
        }
        return game.Phase != GamePhase.GameOver && game.Phase != GamePhase.AssassinVote;
    }

    #endregion

    #region Phase Validation Tests

    [TestMethod]
    public void ProposeTeam_DuringLobby_Throws()
    {
        var game = CreateLobbyGame();
        var host = game.Players[0];

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => game.ProposeTeam(host.Id, [host.Id]));
        StringAssert.Contains(ex.Message, "Lobby");
    }

    [TestMethod]
    public void VoteOnProposal_DuringLobby_Throws()
    {
        var game = CreateLobbyGame();
        var host = game.Players[0];

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => game.VoteOnProposal(host.Id, VoteType.Approve));
        StringAssert.Contains(ex.Message, "Lobby");
    }

    [TestMethod]
    public void Start_WhenAlreadyStarted_Throws()
    {
        var game = CreateStartedGame();
        var host = game.Players.First(p => p.IsHost);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => game.Start(host.Id));
    }

    [TestMethod]
    public void Join_AfterGameStarted_Throws()
    {
        var game = CreateStartedGame();

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => game.Join("LatePlayer"));
        StringAssert.Contains(ex.Message, "Lobby");
    }

    #endregion

    #region Lobby Tests

    [TestMethod]
    public void Join_AddsPlayers_FirstIsHost()
    {
        var game = new Game("test");

        var first = game.Join("Alice");
        var second = game.Join("Bob");

        Assert.AreEqual(2, game.Players.Count);
        Assert.IsTrue(first.IsHost);
        Assert.IsFalse(second.IsHost);
        Assert.AreEqual("Alice", first.Name);
        Assert.AreEqual("Bob", second.Name);
    }

    [TestMethod]
    public void Join_DuplicateName_Throws()
    {
        var game = new Game("test");
        game.Join("Alice");

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => game.Join("Alice"));
        StringAssert.Contains(ex.Message, "Alice");
    }

    [TestMethod]
    public void Join_ExceedsMaxPlayers_Throws()
    {
        var game = new Game("test");
        for (int i = 0; i < GameConfiguration.MaxPlayers; i++)
            game.Join($"Player{i}");

        Assert.ThrowsExactly<InvalidOperationException>(
            () => game.Join("Overflow"));
    }

    [TestMethod]
    public void RemovePlayer_Host_Throws()
    {
        var game = new Game("test");
        var host = game.Join("Host");
        game.Join("Other");

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => game.RemovePlayer(host.Id));
        StringAssert.Contains(ex.Message, "host");
    }

    [TestMethod]
    public void RemovePlayer_NonHost_Succeeds()
    {
        var game = new Game("test");
        game.Join("Host");
        var other = game.Join("Other");

        game.RemovePlayer(other.Id);

        Assert.AreEqual(1, game.Players.Count);
        Assert.IsFalse(game.Players.Any(p => p.Id == other.Id));
    }

    [TestMethod]
    public void UpdateSettings_NonHost_Throws()
    {
        var game = new Game("test");
        game.Join("Host");
        var other = game.Join("Other");

        Assert.ThrowsExactly<InvalidOperationException>(
            () => game.UpdateSettings(other.Id, new GameSettings()));
    }

    [TestMethod]
    public void Start_NonHost_Throws()
    {
        var game = CreateLobbyGame();
        var nonHost = game.Players.First(p => !p.IsHost);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => game.Start(nonHost.Id));
    }

    #endregion

    #region Start Game Tests

    [TestMethod]
    public void Start_WithFewerThan5Players_Throws()
    {
        var game = new Game("test");
        var host = game.Join("Host");
        for (int i = 1; i < 4; i++)
            game.Join($"Player{i}");

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => game.Start(host.Id));
        StringAssert.Contains(ex.Message, "5");
    }

    [TestMethod]
    public void Start_AssignsRolesToAllPlayers()
    {
        var game = CreateLobbyGame();
        var host = game.Players[0];

        game.Start(host.Id);

        Assert.IsTrue(game.Players.All(p => p.Role != null));
        Assert.IsTrue(game.Players.All(p => p.Team != null));
    }

    [TestMethod]
    public void Start_MovesToRoleReveal_ProceedMovesToTeamProposal()
    {
        var game = CreateLobbyGame();
        var host = game.Players[0];

        game.Start(host.Id);
        Assert.AreEqual(GamePhase.RoleReveal, game.Phase);

        game.ProceedFromRoleReveal();
        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);
        Assert.AreEqual(1, game.Rounds.Count);
    }

    #endregion

    #region Team Proposal Tests

    [TestMethod]
    public void ProposeTeam_NonLeader_Throws()
    {
        var game = CreateStartedGame();
        var nonLeader = game.Players.First(p => p.Id != game.CurrentLeader!.Id);
        var teamIds = game.Players.Take(game.CurrentRound!.RequiredTeamSize)
            .Select(p => p.Id).ToList();

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => game.ProposeTeam(nonLeader.Id, teamIds));
        StringAssert.Contains(ex.Message, "leader");
    }

    [TestMethod]
    public void ProposeTeam_WrongTeamSize_Throws()
    {
        var game = CreateStartedGame();
        var leader = game.CurrentLeader!;

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => game.ProposeTeam(leader.Id, [game.Players[0].Id]));
        StringAssert.Contains(ex.Message, "exactly");
    }

    [TestMethod]
    public void ProposeTeam_DuplicatePlayers_Throws()
    {
        var game = CreateStartedGame();
        var leader = game.CurrentLeader!;
        int teamSize = game.CurrentRound!.RequiredTeamSize;
        var dupes = Enumerable.Repeat(game.Players[0].Id, teamSize).ToList();

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => game.ProposeTeam(leader.Id, dupes));
        StringAssert.Contains(ex.Message, "Duplicate");
    }

    [TestMethod]
    public void ProposeTeam_NonExistentPlayer_Throws()
    {
        var game = CreateStartedGame();
        var leader = game.CurrentLeader!;
        int teamSize = game.CurrentRound!.RequiredTeamSize;
        var ids = game.Players.Take(teamSize - 1).Select(p => p.Id).ToList();
        ids.Add("nonexistent-id");

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => game.ProposeTeam(leader.Id, ids));
        StringAssert.Contains(ex.Message, "not found");
    }

    #endregion

    #region Team Vote Tests

    [TestMethod]
    public void VoteOnProposal_AllPlayersMustVote_ApprovedMovesToQuest()
    {
        var game = CreateStartedGame();
        ProposeCurrentTeam(game);

        // Vote all but last player
        for (int i = 0; i < game.Players.Count - 1; i++)
            game.VoteOnProposal(game.Players[i].Id, VoteType.Approve);

        Assert.AreEqual(GamePhase.TeamVote, game.Phase);

        // Last vote resolves it
        game.VoteOnProposal(game.Players[^1].Id, VoteType.Approve);

        Assert.AreEqual(GamePhase.Quest, game.Phase);
    }

    [TestMethod]
    public void VoteOnProposal_MajorityApproved_MovesToQuest()
    {
        var game = CreateStartedGame();
        ProposeCurrentTeam(game);

        // 3 approve, 2 reject → majority approves
        int approveCount = (game.Players.Count / 2) + 1;
        for (int i = 0; i < approveCount; i++)
            game.VoteOnProposal(game.Players[i].Id, VoteType.Approve);
        for (int i = approveCount; i < game.Players.Count; i++)
            game.VoteOnProposal(game.Players[i].Id, VoteType.Reject);

        Assert.AreEqual(GamePhase.Quest, game.Phase);
    }

    [TestMethod]
    public void VoteOnProposal_Rejected_AdvancesLeader_StaysInTeamProposal()
    {
        var game = CreateStartedGame();
        int originalLeaderIndex = game.CurrentLeaderIndex;

        ProposeCurrentTeam(game);
        RejectCurrentProposal(game);

        Assert.AreEqual(GamePhase.TeamProposal, game.Phase);
        Assert.AreNotEqual(originalLeaderIndex, game.CurrentLeaderIndex);
    }

    [TestMethod]
    public void VoteOnProposal_5ConsecutiveRejections_EvilWins()
    {
        var game = CreateStartedGame();

        for (int i = 0; i < GameConfiguration.MaxConsecutiveRejections; i++)
        {
            ProposeCurrentTeam(game);
            RejectCurrentProposal(game);

            if (i < GameConfiguration.MaxConsecutiveRejections - 1)
                Assert.AreEqual(GamePhase.TeamProposal, game.Phase);
        }

        Assert.AreEqual(GamePhase.GameOver, game.Phase);
        Assert.AreEqual(GameResult.EvilWins, game.Result);
    }

    #endregion

    #region Quest Vote Tests

    [TestMethod]
    public void VoteOnQuest_NonParticipant_Throws()
    {
        var game = CreateStartedGame();
        ProposeCurrentTeam(game);
        ApproveCurrentProposal(game);

        var quest = game.CurrentRound!.Quest!;
        var nonParticipant = game.Players.First(p => !quest.ParticipantIds.Contains(p.Id));

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => game.VoteOnQuest(nonParticipant.Id, QuestVote.Success));
        StringAssert.Contains(ex.Message, "not on this quest");
    }

    [TestMethod]
    public void VoteOnQuest_GoodPlayerVotesFail_Throws()
    {
        var game = CreateStartedGame();
        // We need a good player on the quest
        var leader = game.CurrentLeader!;
        int teamSize = game.CurrentRound!.RequiredTeamSize;
        var goodPlayers = game.Players.Where(p => p.Team == Team.Good).ToList();
        var teamIds = goodPlayers.Take(teamSize).Select(p => p.Id).ToList();

        // If we don't have enough good players, fill with others
        if (teamIds.Count < teamSize)
        {
            var remaining = game.Players.Where(p => !teamIds.Contains(p.Id))
                .Take(teamSize - teamIds.Count).Select(p => p.Id);
            teamIds.AddRange(remaining);
        }

        game.ProposeTeam(leader.Id, teamIds);
        ApproveCurrentProposal(game);

        var goodOnQuest = game.Players.First(p =>
            p.Team == Team.Good && teamIds.Contains(p.Id));

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => game.VoteOnQuest(goodOnQuest.Id, QuestVote.Fail));
        StringAssert.Contains(ex.Message, "Good");
    }

    [TestMethod]
    public void VoteOnQuest_AllSuccess_QuestSucceeds()
    {
        var game = CreateStartedGame();
        ProposeCurrentTeam(game);
        ApproveCurrentProposal(game);

        CompleteQuestAllSuccess(game);

        Assert.AreEqual(GamePhase.QuestResult, game.Phase);
        Assert.IsTrue(game.CurrentRound!.IsSuccess);
        Assert.AreEqual(1, game.CompletedQuestsGood);
    }

    [TestMethod]
    public void VoteOnQuest_FailsReachRequired_QuestFails()
    {
        var game = CreateStartedGame();
        ProposeTeamWithEvil(game);
        ApproveCurrentProposal(game);

        CompleteQuestWithFail(game);

        Assert.AreEqual(GamePhase.QuestResult, game.Phase);
        Assert.IsFalse(game.CurrentRound!.IsSuccess);
        Assert.AreEqual(1, game.CompletedQuestsEvil);
    }

    #endregion

    #region Leader Rotation Tests

    [TestMethod]
    public void LeaderAdvancesAfterEachProposal()
    {
        var game = CreateStartedGame();

        var firstLeader = game.CurrentLeader;
        int firstIndex = game.CurrentLeaderIndex;

        // Propose and reject to trigger leader advance
        ProposeCurrentTeam(game);
        RejectCurrentProposal(game);

        int secondIndex = game.CurrentLeaderIndex;
        Assert.AreEqual((firstIndex + 1) % game.Players.Count, secondIndex);

        // Propose and approve also advances leader for next round
        ProposeCurrentTeam(game);
        ApproveCurrentProposal(game);

        // Complete quest to get to next round
        CompleteQuestAllSuccess(game);
        game.ProceedFromQuestResult();

        int thirdIndex = game.CurrentLeaderIndex;
        Assert.AreEqual((secondIndex + 1) % game.Players.Count, thirdIndex);
    }

    #endregion

    #region Win Condition Tests

    [TestMethod]
    public void ThreeSuccessfulQuests_WithMerlin_MovesToAssassinVote()
    {
        var game = CreateStartedGame();
        Assert.IsTrue(game.Settings.MerlinEnabled, "Merlin should be enabled by default");

        for (int i = 0; i < 3; i++)
        {
            ProposeCurrentTeam(game);
            ApproveCurrentProposal(game);
            CompleteQuestAllSuccess(game);

            if (i < 2)
                game.ProceedFromQuestResult();
            else
            {
                // Third quest success should lead to AssassinVote via ProceedFromQuestResult
                game.ProceedFromQuestResult();
            }
        }

        Assert.AreEqual(3, game.CompletedQuestsGood);
        Assert.AreEqual(GamePhase.AssassinVote, game.Phase);
    }

    [TestMethod]
    public void ThreeSuccessfulQuests_WithoutMerlin_GoodWins()
    {
        var game = new Game("test", new RoleAssigner(new Random(42)));
        for (int i = 0; i < 5; i++)
            game.Join($"Player{i}");

        game.UpdateSettings(game.Players[0].Id, new GameSettings
        {
            MerlinEnabled = false,
            AssassinEnabled = false
        });

        game.Start(game.Players[0].Id);
        game.ProceedFromRoleReveal();

        for (int i = 0; i < 3; i++)
        {
            ProposeCurrentTeam(game);
            ApproveCurrentProposal(game);
            CompleteQuestAllSuccess(game);

            if (game.Phase == GamePhase.QuestResult)
                game.ProceedFromQuestResult();
        }

        Assert.AreEqual(3, game.CompletedQuestsGood);
        Assert.AreEqual(GamePhase.GameOver, game.Phase);
        Assert.AreEqual(GameResult.GoodWins, game.Result);
    }

    [TestMethod]
    public void ThreeFailedQuests_EvilWins()
    {
        var game = CreateStartedGame();

        for (int i = 0; i < 3; i++)
        {
            ProposeTeamWithEvil(game);
            ApproveCurrentProposal(game);
            CompleteQuestWithFail(game);

            if (game.Phase == GamePhase.QuestResult)
                game.ProceedFromQuestResult();
        }

        Assert.AreEqual(3, game.CompletedQuestsEvil);
        Assert.AreEqual(GamePhase.GameOver, game.Phase);
        Assert.AreEqual(GameResult.EvilWins, game.Result);
    }

    #endregion

    #region Assassin Tests

    [TestMethod]
    public void Assassinate_TargetMerlin_EvilWinsByAssassination()
    {
        var game = CreateStartedGame();

        // Win 3 quests for good to reach AssassinVote
        for (int i = 0; i < 3; i++)
        {
            ProposeCurrentTeam(game);
            ApproveCurrentProposal(game);
            CompleteQuestAllSuccess(game);
            game.ProceedFromQuestResult();
        }

        Assert.AreEqual(GamePhase.AssassinVote, game.Phase);

        var assassin = game.Players.First(p => p.Role == Role.Assassin);
        var merlin = game.Players.First(p => p.Role == Role.Merlin);

        game.Assassinate(assassin.Id, merlin.Id);

        Assert.AreEqual(GamePhase.GameOver, game.Phase);
        Assert.AreEqual(GameResult.EvilWinsByAssassination, game.Result);
    }

    [TestMethod]
    public void Assassinate_TargetNonMerlin_GoodWins()
    {
        var game = CreateStartedGame();

        for (int i = 0; i < 3; i++)
        {
            ProposeCurrentTeam(game);
            ApproveCurrentProposal(game);
            CompleteQuestAllSuccess(game);
            game.ProceedFromQuestResult();
        }

        Assert.AreEqual(GamePhase.AssassinVote, game.Phase);

        var assassin = game.Players.First(p => p.Role == Role.Assassin);
        var nonMerlinGood = game.Players.First(p =>
            p.Team == Team.Good && p.Role != Role.Merlin);

        game.Assassinate(assassin.Id, nonMerlinGood.Id);

        Assert.AreEqual(GamePhase.GameOver, game.Phase);
        Assert.AreEqual(GameResult.GoodWins, game.Result);
    }

    #endregion
}
