using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Avalon.Web.Tests;

[TestClass]
public class GamesControllerTests
{
    private static WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _factory = new WebApplicationFactory<Program>();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _factory?.Dispose();
    }

    [TestInitialize]
    public void TestInit()
    {
        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _client?.Dispose();
    }

    #region Helpers

    private async Task<(string GameId, string HostId)> CreateGameAsync(string hostName = "Alice")
    {
        var response = await _client.PostAsJsonAsync($"/api/games?hostName={hostName}", new { });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("gameId").GetString()!, json.GetProperty("hostPlayerId").GetString()!);
    }

    private async Task<string> JoinGameAsync(string gameId, string playerName)
    {
        var response = await _client.PostAsJsonAsync($"/api/games/{gameId}/join", new { PlayerName = playerName });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("playerId").GetString()!;
    }

    private async Task<JsonElement> GetGameStateAsync(string gameId, string playerId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/games/{gameId}");
        request.Headers.Add("X-Player-Id", playerId);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> SendWithPlayerHeader(HttpMethod method, string url, string playerId, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Player-Id", playerId);
        if (body != null)
            request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    /// <summary>
    /// Creates a game with 5 players and starts it. Returns game id, host id, and all player ids.
    /// </summary>
    private async Task<(string GameId, string HostId, List<string> AllPlayerIds)> CreateAndStartGameWith5PlayersAsync()
    {
        var (gameId, hostId) = await CreateGameAsync("Alice");
        var playerIds = new List<string> { hostId };

        foreach (var name in new[] { "Bob", "Charlie", "Diana", "Eve" })
        {
            var pid = await JoinGameAsync(gameId, name);
            playerIds.Add(pid);
        }

        var startResponse = await SendWithPlayerHeader(HttpMethod.Post, $"/api/games/{gameId}/start", hostId);
        startResponse.EnsureSuccessStatusCode();

        return (gameId, hostId, playerIds);
    }

    #endregion

    [TestMethod]
    public async Task CreateGame_ReturnsCreatedWithGameId()
    {
        var response = await _client.PostAsJsonAsync("/api/games?hostName=Alice", new { });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(string.IsNullOrEmpty(json.GetProperty("gameId").GetString()));
        Assert.IsFalse(string.IsNullOrEmpty(json.GetProperty("hostPlayerId").GetString()));
        Assert.IsFalse(string.IsNullOrEmpty(json.GetProperty("hostPlayerToken").GetString()));
        Assert.IsFalse(string.IsNullOrEmpty(json.GetProperty("joinLink").GetString()));
    }

    [TestMethod]
    public async Task GetGame_ReturnsGameState()
    {
        var (gameId, hostId) = await CreateGameAsync();

        var state = await GetGameStateAsync(gameId, hostId);

        Assert.AreEqual(gameId, state.GetProperty("gameId").GetString());
        Assert.AreEqual("Lobby", state.GetProperty("phase").GetString());
    }

    [TestMethod]
    public async Task GetGame_NotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/games/nonexistent-game-id");
        request.Headers.Add("X-Player-Id", "some-player");
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task JoinGame_AddsPlayer()
    {
        var (gameId, _) = await CreateGameAsync();

        var response = await _client.PostAsJsonAsync($"/api/games/{gameId}/join", new { PlayerName = "Bob" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(string.IsNullOrEmpty(json.GetProperty("playerId").GetString()));
        Assert.IsFalse(string.IsNullOrEmpty(json.GetProperty("playerToken").GetString()));
    }

    [TestMethod]
    public async Task JoinGame_DuplicateName_ReturnsBadRequest()
    {
        var (gameId, _) = await CreateGameAsync();
        await JoinGameAsync(gameId, "Bob");

        var response = await _client.PostAsJsonAsync($"/api/games/{gameId}/join", new { PlayerName = "Bob" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task JoinGame_NotFound()
    {
        var response = await _client.PostAsJsonAsync("/api/games/nonexistent-game-id/join", new { PlayerName = "Bob" });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateSettings_AsHost()
    {
        var (gameId, hostId) = await CreateGameAsync();

        var settings = new
        {
            MerlinEnabled = true,
            AssassinEnabled = true,
            PercivalEnabled = false,
            MorganaEnabled = false,
            MordredEnabled = false,
            OberonEnabled = false,
            LadyOfTheLakeEnabled = false
        };
        var response = await SendWithPlayerHeader(HttpMethod.Post, $"/api/games/{gameId}/settings", hostId, settings);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task StartGame_AsHost()
    {
        var (gameId, hostId) = await CreateGameAsync();
        foreach (var name in new[] { "Bob", "Charlie", "Diana", "Eve" })
            await JoinGameAsync(gameId, name);

        var response = await SendWithPlayerHeader(HttpMethod.Post, $"/api/games/{gameId}/start", hostId);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);

        var state = await GetGameStateAsync(gameId, hostId);
        Assert.AreEqual("TeamProposal", state.GetProperty("phase").GetString());
    }

    [TestMethod]
    public async Task StartGame_NotEnoughPlayers_ReturnsBadRequest()
    {
        var (gameId, hostId) = await CreateGameAsync();
        await JoinGameAsync(gameId, "Bob");

        var response = await SendWithPlayerHeader(HttpMethod.Post, $"/api/games/{gameId}/start", hostId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ProposeTeam_AndVote()
    {
        var (gameId, hostId, allPlayerIds) = await CreateAndStartGameWith5PlayersAsync();

        // Find the current leader
        var state = await GetGameStateAsync(gameId, hostId);
        var leaderId = state.GetProperty("currentLeader").GetString()!;

        // Propose a team of 2 (quest 1 for 5 players requires 2)
        var teamIds = allPlayerIds.Take(2).ToList();
        var proposeResponse = await SendWithPlayerHeader(
            HttpMethod.Post, $"/api/games/{gameId}/propose", leaderId, new { PlayerIds = teamIds });
        Assert.AreEqual(HttpStatusCode.NoContent, proposeResponse.StatusCode);

        // All players vote Approve
        foreach (var playerId in allPlayerIds)
        {
            var voteResponse = await SendWithPlayerHeader(
                HttpMethod.Post, $"/api/games/{gameId}/vote", playerId, new { Vote = "Approve" });
            Assert.AreEqual(HttpStatusCode.NoContent, voteResponse.StatusCode);
        }

        // After all votes, game should move to Quest phase
        var stateAfterVote = await GetGameStateAsync(gameId, hostId);
        Assert.AreEqual("Quest", stateAfterVote.GetProperty("phase").GetString());
    }

    [TestMethod]
    public async Task FullGameFlow_FirstQuest()
    {
        var (gameId, hostId, allPlayerIds) = await CreateAndStartGameWith5PlayersAsync();

        // 1. Get game state to find current leader
        var state = await GetGameStateAsync(gameId, hostId);
        Assert.AreEqual("TeamProposal", state.GetProperty("phase").GetString());
        var leaderId = state.GetProperty("currentLeader").GetString()!;

        // 2. Propose team of 2 (quest 1 for 5 players)
        var teamIds = allPlayerIds.Take(2).ToList();
        var proposeResponse = await SendWithPlayerHeader(
            HttpMethod.Post, $"/api/games/{gameId}/propose", leaderId, new { PlayerIds = teamIds });
        Assert.AreEqual(HttpStatusCode.NoContent, proposeResponse.StatusCode);

        // 3. All players vote Approve
        foreach (var playerId in allPlayerIds)
        {
            var voteResponse = await SendWithPlayerHeader(
                HttpMethod.Post, $"/api/games/{gameId}/vote", playerId, new { Vote = "Approve" });
            Assert.AreEqual(HttpStatusCode.NoContent, voteResponse.StatusCode);
        }

        // 4. Verify Quest phase
        state = await GetGameStateAsync(gameId, hostId);
        Assert.AreEqual("Quest", state.GetProperty("phase").GetString());

        // 5. Quest team members vote Success
        foreach (var questPlayerId in teamIds)
        {
            var questVoteResponse = await SendWithPlayerHeader(
                HttpMethod.Post, $"/api/games/{gameId}/quest-vote", questPlayerId, new { Vote = "Success" });
            Assert.AreEqual(HttpStatusCode.NoContent, questVoteResponse.StatusCode);
        }

        // 6. Verify QuestResult phase
        state = await GetGameStateAsync(gameId, hostId);
        Assert.AreEqual("QuestResult", state.GetProperty("phase").GetString());

        // 7. Proceed from quest result
        var proceedResponse = await _client.PostAsJsonAsync($"/api/games/{gameId}/proceed", new { });
        Assert.AreEqual(HttpStatusCode.NoContent, proceedResponse.StatusCode);

        // 8. Should be back to TeamProposal for the next round
        state = await GetGameStateAsync(gameId, hostId);
        Assert.AreEqual("TeamProposal", state.GetProperty("phase").GetString());
    }
}
