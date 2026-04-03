using Avalon.Application.DTOs;
using Avalon.Application.Interfaces;
using Avalon.Application.Services;
using Avalon.Domain.Enums;
using Avalon.Domain.Models;
using Avalon.Infrastructure.Persistence;
using Moq;

namespace Avalon.Application.Tests;

[TestClass]
public class GameServiceTests
{
    private InMemoryGameRepository _repository = null!;
    private Mock<IGameNotifier> _notifierMock = null!;
    private GameStateMapper _mapper = null!;
    private GameService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _repository = new InMemoryGameRepository();
        _notifierMock = new Mock<IGameNotifier>();
        _mapper = new GameStateMapper();
        _service = new GameService(_repository, _notifierMock.Object, _mapper);
    }

    [TestMethod]
    public async Task CreateGameAsync_CreatesGameAndReturnsValidResponse()
    {
        var response = await _service.CreateGameAsync("Alice");

        Assert.IsNotNull(response);
        Assert.IsFalse(string.IsNullOrEmpty(response.GameId));
        Assert.IsFalse(string.IsNullOrEmpty(response.HostPlayerId));
        Assert.IsFalse(string.IsNullOrEmpty(response.HostPlayerToken));
        Assert.IsTrue(response.JoinLink.Contains(response.GameId));

        var game = await _repository.GetByIdAsync(response.GameId);
        Assert.IsNotNull(game);
        Assert.AreEqual(1, game.Players.Count);
        Assert.AreEqual("Alice", game.Players[0].Name);
        Assert.IsTrue(game.Players[0].IsHost);
    }

    [TestMethod]
    public async Task JoinGameAsync_AddsPlayerAndNotifies()
    {
        var createResponse = await _service.CreateGameAsync("Alice");

        var joinResponse = await _service.JoinGameAsync(createResponse.GameId, "Bob");

        Assert.IsNotNull(joinResponse);
        Assert.IsFalse(string.IsNullOrEmpty(joinResponse.PlayerId));
        Assert.IsFalse(string.IsNullOrEmpty(joinResponse.PlayerToken));

        var game = await _repository.GetByIdAsync(createResponse.GameId);
        Assert.AreEqual(2, game!.Players.Count);
        Assert.AreEqual("Bob", game.Players[1].Name);

        _notifierMock.Verify(n => n.NotifyPlayerJoined(createResponse.GameId, "Bob"), Times.Once);
    }

    [TestMethod]
    public async Task GetGameStateAsync_ReturnsGameState()
    {
        var createResponse = await _service.CreateGameAsync("Alice");

        var state = await _service.GetGameStateAsync(createResponse.GameId, createResponse.HostPlayerId);

        Assert.IsNotNull(state);
        Assert.AreEqual(createResponse.GameId, state.GameId);
        Assert.AreEqual("Lobby", state.Phase);
        Assert.AreEqual(1, state.Players.Count);
        Assert.AreEqual("Alice", state.Players[0].Name);
        Assert.IsTrue(state.Players[0].IsHost);
    }

    [TestMethod]
    public async Task GetGameStateAsync_ThrowsKeyNotFoundExceptionForUnknownGame()
    {
        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _service.GetGameStateAsync("NONEXIST", null));
    }

    [TestMethod]
    public async Task UpdateSettingsAsync_UpdatesGameSettings()
    {
        var createResponse = await _service.CreateGameAsync("Alice");
        var request = new UpdateSettingsRequest(
            MerlinEnabled: true,
            AssassinEnabled: true,
            PercivalEnabled: true,
            MorganaEnabled: true,
            MordredEnabled: false,
            OberonEnabled: false,
            LadyOfTheLakeEnabled: false);

        await _service.UpdateSettingsAsync(
            createResponse.GameId, createResponse.HostPlayerId, request);

        var game = await _repository.GetByIdAsync(createResponse.GameId);
        Assert.IsTrue(game!.Settings.MerlinEnabled);
        Assert.IsTrue(game.Settings.AssassinEnabled);
        Assert.IsTrue(game.Settings.PercivalEnabled);
        Assert.IsTrue(game.Settings.MorganaEnabled);
        Assert.IsFalse(game.Settings.MordredEnabled);
        Assert.IsFalse(game.Settings.OberonEnabled);
        Assert.IsFalse(game.Settings.LadyOfTheLakeEnabled);
    }

    [TestMethod]
    public async Task StartGameAsync_StartsGameAndNotifies()
    {
        var createResponse = await _service.CreateGameAsync("Alice");
        for (int i = 1; i < 5; i++)
            await _service.JoinGameAsync(createResponse.GameId, $"Player{i}");

        await _service.StartGameAsync(createResponse.GameId, createResponse.HostPlayerId);

        var game = await _repository.GetByIdAsync(createResponse.GameId);
        Assert.AreEqual(GamePhase.TeamProposal, game!.Phase);
        Assert.AreEqual(1, game.Rounds.Count);
        Assert.IsTrue(game.Players.All(p => p.Role != null));
        Assert.IsTrue(game.Players.All(p => p.Team != null));

        _notifierMock.Verify(
            n => n.NotifyGameStarted(createResponse.GameId), Times.Once);
        _notifierMock.Verify(
            n => n.NotifyPhaseChanged(createResponse.GameId, "TeamProposal"), Times.Once);
    }

    [TestMethod]
    public async Task FullGameFlow_CreateJoinStartProposeVoteQuest()
    {
        // Create game
        var createResponse = await _service.CreateGameAsync("Alice");
        var gameId = createResponse.GameId;
        var hostId = createResponse.HostPlayerId;

        // Join 4 more players (5 total)
        var playerIds = new List<string> { hostId };
        for (int i = 1; i < 5; i++)
        {
            var joinResponse = await _service.JoinGameAsync(gameId, $"Player{i}");
            playerIds.Add(joinResponse.PlayerId);
        }

        // Start game
        await _service.StartGameAsync(gameId, hostId);
        var game = await _repository.GetByIdAsync(gameId);
        Assert.AreEqual(GamePhase.TeamProposal, game!.Phase);

        // Propose a team (round 1 requires 2 players for 5-player game)
        var leaderId = game.CurrentLeader!.Id;
        var teamMembers = game.Players.Take(2).Select(p => p.Id).ToList();
        await _service.ProposeTeamAsync(gameId, leaderId, teamMembers);

        game = await _repository.GetByIdAsync(gameId);
        Assert.AreEqual(GamePhase.TeamVote, game!.Phase);

        // All players approve
        foreach (var pid in playerIds)
            await _service.VoteOnProposalAsync(gameId, pid, VoteType.Approve);

        game = await _repository.GetByIdAsync(gameId);
        Assert.AreEqual(GamePhase.Quest, game!.Phase);

        // Quest members vote success
        foreach (var pid in teamMembers)
            await _service.VoteOnQuestAsync(gameId, pid, QuestVote.Success);

        game = await _repository.GetByIdAsync(gameId);
        Assert.AreEqual(GamePhase.QuestResult, game!.Phase);
        Assert.IsTrue(game.CurrentRound!.Quest!.IsSuccess);

        // Verify notifications
        _notifierMock.Verify(
            n => n.NotifyTeamProposed(gameId, It.IsAny<string>(), It.IsAny<List<string>>()), Times.Once);
        _notifierMock.Verify(
            n => n.NotifyVoteRevealed(gameId, It.IsAny<Dictionary<string, string>>()), Times.Once);
        _notifierMock.Verify(
            n => n.NotifyQuestResult(gameId, 2, 0), Times.Once);
    }
}

[TestClass]
public class GameStateMapperTests
{
    private GameStateMapper _mapper = null!;

    [TestInitialize]
    public void Setup()
    {
        _mapper = new GameStateMapper();
    }

    /// <summary>
    /// Creates a 10-player game in TeamProposal phase with all special roles enabled.
    /// 6 Good (Merlin, Percival, 4× LoyalServant), 4 Evil (Assassin, Morgana, Mordred, Oberon).
    /// </summary>
    private static Game CreateStartedGameWithAllRoles()
    {
        var roleAssigner = new RoleAssigner(new Random(42));
        var game = new Game("test-game", roleAssigner);

        var host = game.Join("Host");
        for (int i = 1; i < 10; i++)
            game.Join($"Player{i}");

        var settings = new GameSettings
        {
            MerlinEnabled = true,
            AssassinEnabled = true,
            PercivalEnabled = true,
            MorganaEnabled = true,
            MordredEnabled = true,
            OberonEnabled = true,
            LadyOfTheLakeEnabled = false,
        };
        game.UpdateSettings(host.Id, settings);
        game.Start(host.Id);
        game.ProceedFromRoleReveal();

        return game;
    }

    /// <summary>
    /// Creates a game and forces GameOver via 5 consecutive proposal rejections.
    /// </summary>
    private static Game CreateGameOverGame()
    {
        var game = CreateStartedGameWithAllRoles();

        for (int r = 0; r < 5; r++)
        {
            var leader = game.CurrentLeader!;
            var teamSize = game.CurrentRound!.RequiredTeamSize;
            var teamIds = game.Players.Take(teamSize).Select(p => p.Id).ToList();
            game.ProposeTeam(leader.Id, teamIds);

            foreach (var p in game.Players)
                game.VoteOnProposal(p.Id, VoteType.Reject);
        }

        return game;
    }

    [TestMethod]
    public void MerlinSeesEvilExceptMordred()
    {
        var game = CreateStartedGameWithAllRoles();
        var merlin = game.Players.First(p => p.Role == Role.Merlin);
        var mordred = game.Players.First(p => p.Role == Role.Mordred);
        var expectedVisible = game.Players
            .Where(p => p.Team == Team.Evil && p.Role != Role.Mordred)
            .Select(p => p.Id).ToList();

        var response = _mapper.MapToResponse(game, merlin.Id);

        Assert.IsNotNull(response.VisiblePlayerIds);
        CollectionAssert.AreEquivalent(expectedVisible, response.VisiblePlayerIds);
        Assert.IsFalse(response.VisiblePlayerIds.Contains(mordred.Id));
        Assert.IsFalse(response.VisiblePlayerIds.Contains(merlin.Id));
    }

    [TestMethod]
    public void EvilPlayerSeesOtherEvilExceptOberon()
    {
        var game = CreateStartedGameWithAllRoles();
        var assassin = game.Players.First(p => p.Role == Role.Assassin);
        var oberon = game.Players.First(p => p.Role == Role.Oberon);
        var expectedVisible = game.Players
            .Where(p => p.Id != assassin.Id && p.Team == Team.Evil && p.Role != Role.Oberon)
            .Select(p => p.Id).ToList();

        var response = _mapper.MapToResponse(game, assassin.Id);

        Assert.IsNotNull(response.VisiblePlayerIds);
        CollectionAssert.AreEquivalent(expectedVisible, response.VisiblePlayerIds);
        Assert.IsFalse(response.VisiblePlayerIds.Contains(oberon.Id));
        Assert.IsFalse(response.VisiblePlayerIds.Contains(assassin.Id));
    }

    [TestMethod]
    public void PercivalSeesMerlinAndMorgana()
    {
        var game = CreateStartedGameWithAllRoles();
        var percival = game.Players.First(p => p.Role == Role.Percival);
        var merlin = game.Players.First(p => p.Role == Role.Merlin);
        var morgana = game.Players.First(p => p.Role == Role.Morgana);

        var response = _mapper.MapToResponse(game, percival.Id);

        Assert.IsNotNull(response.VisiblePlayerIds);
        Assert.AreEqual(2, response.VisiblePlayerIds.Count);
        Assert.IsTrue(response.VisiblePlayerIds.Contains(merlin.Id));
        Assert.IsTrue(response.VisiblePlayerIds.Contains(morgana.Id));
    }

    [TestMethod]
    public void LoyalServantGetsEmptyVisiblePlayerIds()
    {
        var game = CreateStartedGameWithAllRoles();
        var loyalServant = game.Players.First(p => p.Role == Role.LoyalServant);

        var response = _mapper.MapToResponse(game, loyalServant.Id);

        Assert.IsNotNull(response.VisiblePlayerIds);
        Assert.AreEqual(0, response.VisiblePlayerIds.Count);
    }

    [TestMethod]
    public void OberonGetsEmptyVisiblePlayerIds()
    {
        var game = CreateStartedGameWithAllRoles();
        var oberon = game.Players.First(p => p.Role == Role.Oberon);

        var response = _mapper.MapToResponse(game, oberon.Id);

        Assert.IsNotNull(response.VisiblePlayerIds);
        Assert.AreEqual(0, response.VisiblePlayerIds.Count);
    }

    [TestMethod]
    public void DuringActiveGame_PlayerViewDoesNotShowRolesOrTeams()
    {
        var game = CreateStartedGameWithAllRoles();
        var anyPlayerId = game.Players[0].Id;

        var response = _mapper.MapToResponse(game, anyPlayerId);

        foreach (var playerView in response.Players)
        {
            Assert.IsNull(playerView.Role,
                $"Player {playerView.Name} should not have visible Role during active game");
            Assert.IsNull(playerView.Team,
                $"Player {playerView.Name} should not have visible Team during active game");
        }
    }

    [TestMethod]
    public void AtGameOver_PlayerViewShowsAllRolesAndTeams()
    {
        var game = CreateGameOverGame();
        Assert.AreEqual(GamePhase.GameOver, game.Phase);
        var anyPlayerId = game.Players[0].Id;

        var response = _mapper.MapToResponse(game, anyPlayerId);

        foreach (var playerView in response.Players)
        {
            Assert.IsNotNull(playerView.Role,
                $"Player {playerView.Name} should have visible Role at GameOver");
            Assert.IsNotNull(playerView.Team,
                $"Player {playerView.Name} should have visible Team at GameOver");
        }
    }

    [TestMethod]
    public void RequestingPlayerGetsOwnRoleAndTeam()
    {
        var game = CreateStartedGameWithAllRoles();
        var merlin = game.Players.First(p => p.Role == Role.Merlin);

        var response = _mapper.MapToResponse(game, merlin.Id);

        Assert.AreEqual(merlin.Id, response.YourPlayerId);
        Assert.AreEqual("Merlin", response.YourRole);
        Assert.AreEqual("Good", response.YourTeam);
    }
}
