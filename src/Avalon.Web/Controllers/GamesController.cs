using Microsoft.AspNetCore.Mvc;
using Avalon.Application.DTOs;
using Avalon.Application.Services;
using Avalon.Domain.Enums;

namespace Avalon.Web.Controllers;

[ApiController]
[Route("api/games")]
public class GamesController : ControllerBase
{
    private readonly GameService _gameService;

    public GamesController(GameService gameService)
    {
        _gameService = gameService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateGameResponse>> CreateGame([FromBody] CreateGameRequest request, [FromQuery] string hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
            return BadRequest("hostName query parameter is required.");

        var response = await _gameService.CreateGameAsync(hostName);
        return CreatedAtAction(nameof(GetGameState), new { id = response.GameId }, response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GameStateResponse>> GetGameState(string id, [FromHeader(Name = "X-Player-Id")] string? playerId)
    {
        try
        {
            var state = await _gameService.GetGameStateAsync(id, playerId);
            return Ok(state);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id}/join")]
    public async Task<ActionResult<JoinGameResponse>> JoinGame(string id, [FromBody] JoinGameRequest request)
    {
        try
        {
            var response = await _gameService.JoinGameAsync(id, request.PlayerName);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id}/settings")]
    public async Task<IActionResult> UpdateSettings(string id, [FromBody] UpdateSettingsRequest request,
        [FromHeader(Name = "X-Player-Id")] string playerId)
    {
        try
        {
            await _gameService.UpdateSettingsAsync(id, playerId, request);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id}/start")]
    public async Task<IActionResult> StartGame(string id, [FromHeader(Name = "X-Player-Id")] string playerId)
    {
        try
        {
            await _gameService.StartGameAsync(id, playerId);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id}/propose")]
    public async Task<IActionResult> ProposeTeam(string id, [FromBody] ProposeTeamRequest request,
        [FromHeader(Name = "X-Player-Id")] string playerId)
    {
        try
        {
            await _gameService.ProposeTeamAsync(id, playerId, request.PlayerIds);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id}/vote")]
    public async Task<IActionResult> Vote(string id, [FromBody] VoteRequest request,
        [FromHeader(Name = "X-Player-Id")] string playerId)
    {
        try
        {
            if (!Enum.TryParse<VoteType>(request.Vote, true, out var vote))
                return BadRequest("Vote must be 'Approve' or 'Reject'.");

            await _gameService.VoteOnProposalAsync(id, playerId, vote);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id}/quest-vote")]
    public async Task<IActionResult> QuestVote(string id, [FromBody] QuestVoteRequest request,
        [FromHeader(Name = "X-Player-Id")] string playerId)
    {
        try
        {
            if (!Enum.TryParse<QuestVote>(request.Vote, true, out var vote))
                return BadRequest("Vote must be 'Success' or 'Fail'.");

            await _gameService.VoteOnQuestAsync(id, playerId, vote);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id}/proceed")]
    public async Task<IActionResult> ProceedFromQuestResult(string id)
    {
        try
        {
            await _gameService.ProceedFromQuestResultAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id}/assassinate")]
    public async Task<IActionResult> Assassinate(string id, [FromBody] AssassinateRequest request,
        [FromHeader(Name = "X-Player-Id")] string playerId)
    {
        try
        {
            await _gameService.AssassinateAsync(id, playerId, request.TargetPlayerId);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id}/lady-investigate")]
    public async Task<ActionResult<string>> LadyInvestigate(string id, [FromBody] LadyInvestigateRequest request,
        [FromHeader(Name = "X-Player-Id")] string playerId)
    {
        try
        {
            var result = await _gameService.InvestigateWithLadyAsync(id, playerId, request.TargetPlayerId);
            return Ok(new { Alignment = result });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
