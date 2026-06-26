using Avalon.Domain.Configuration;
using Avalon.Domain.Enums;
using Avalon.Domain.Models;
using Avalon.Application.DTOs;
using Avalon.Application.Interfaces;

namespace Avalon.Application.Services;

public class GameService
{
    private readonly IGameRepository _repository;
    private readonly IGameNotifier _notifier;
    private readonly GameStateMapper _mapper;
    private readonly BotService _botService;

    public GameService(IGameRepository repository, IGameNotifier notifier, GameStateMapper mapper, BotService botService)
    {
        _repository = repository;
        _notifier = notifier;
        _mapper = mapper;
        _botService = botService;
    }

    public async Task<CreateGameResponse> CreateGameAsync(string hostName)
    {
        var gameId = GenerateGameId();
        var game = new Game(gameId);
        var host = game.Join(hostName);

        await _repository.SaveAsync(game);
        return new CreateGameResponse(gameId, host.Id, host.Id, $"/game/{gameId}");
    }

    public async Task<JoinGameResponse> JoinGameAsync(string gameId, string playerName)
    {
        var game = await GetGameOrThrow(gameId);
        var countBefore = game.Players.Count;
        var player = game.Join(playerName);
        await _repository.SaveAsync(game);

        // Only notify if a new player was added (not a re-join)
        if (game.Players.Count > countBefore)
            await _notifier.NotifyPlayerJoined(gameId, playerName);

        return new JoinGameResponse(player.Id, player.Id);
    }

    public async Task<GameStateResponse> GetGameStateAsync(string gameId, string? playerId)
    {
        var game = await GetGameOrThrow(gameId);
        return _mapper.MapToResponse(game, playerId);
    }

    public async Task UpdateSettingsAsync(string gameId, string hostPlayerId, UpdateSettingsRequest request)
    {
        var game = await GetGameOrThrow(gameId);
        var settings = new GameSettings
        {
            MerlinEnabled = request.MerlinEnabled,
            AssassinEnabled = request.AssassinEnabled,
            PercivalEnabled = request.PercivalEnabled,
            MorganaEnabled = request.MorganaEnabled,
            MordredEnabled = request.MordredEnabled,
            OberonEnabled = request.OberonEnabled,
            LadyOfTheLakeEnabled = request.LadyOfTheLakeEnabled,
        };
        game.UpdateSettings(hostPlayerId, settings);
        await _repository.SaveAsync(game);
    }

    public async Task StartGameAsync(string gameId, string hostPlayerId)
    {
        var game = await GetGameOrThrow(gameId);
        game.Start(hostPlayerId);
        game.ProceedFromRoleReveal();
        await _repository.SaveAsync(game);

        await _notifier.NotifyGameStarted(gameId);
        await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());

        // Process any bot actions (e.g., if bot is first leader)
        await _botService.ProcessBotActionsAsync(gameId);
    }

    public async Task ProposeTeamAsync(string gameId, string playerId, List<string> proposedPlayerIds)
    {
        var game = await GetGameOrThrow(gameId);
        game.ProposeTeam(playerId, proposedPlayerIds);
        await _repository.SaveAsync(game);

        var leaderName = game.Players.First(p => p.Id == playerId).Name;
        var proposedNames = proposedPlayerIds.Select(id => game.Players.First(p => p.Id == id).Name).ToList();
        await _notifier.NotifyTeamProposed(gameId, leaderName, proposedNames);
        await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());

        await _botService.ProcessBotActionsAsync(gameId);
    }

    public async Task VoteOnProposalAsync(string gameId, string playerId, VoteType vote)
    {
        var game = await GetGameOrThrow(gameId);
        var previousPhase = game.Phase;
        game.VoteOnProposal(playerId, vote);
        await _repository.SaveAsync(game);

        if (game.Phase != previousPhase)
        {
            var proposal = game.CurrentRound!.Proposals[^1];
            if (proposal.IsApproved.HasValue)
            {
                var votes = proposal.Votes.ToDictionary(
                    kv => game.Players.First(p => p.Id == kv.Key).Name,
                    kv => kv.Value.ToString());
                await _notifier.NotifyVoteRevealed(gameId, votes);
            }
            await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());
        }

        await _botService.ProcessBotActionsAsync(gameId);
    }

    public async Task VoteOnQuestAsync(string gameId, string playerId, QuestVote vote)
    {
        var game = await GetGameOrThrow(gameId);
        var previousPhase = game.Phase;
        game.VoteOnQuest(playerId, vote);
        await _repository.SaveAsync(game);

        if (game.Phase != previousPhase)
        {
            var quest = game.CurrentRound!.Quest!;
            await _notifier.NotifyQuestResult(gameId, quest.SuccessCount, quest.FailCount);
            await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());
        }

        await _botService.ProcessBotActionsAsync(gameId);
    }

    public async Task ProceedFromQuestResultAsync(string gameId)
    {
        var game = await GetGameOrThrow(gameId);
        game.ProceedFromQuestResult();
        await _repository.SaveAsync(game);

        await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());

        if (game.Phase == GamePhase.GameOver)
            await _notifier.NotifyGameOver(gameId, game.Result!.ToString()!);

        await _botService.ProcessBotActionsAsync(gameId);
    }

    public async Task<string> InvestigateWithLadyAsync(string gameId, string investigatorId, string targetId)
    {
        var game = await GetGameOrThrow(gameId);
        var team = game.InvestigateWithLady(investigatorId, targetId);
        await _repository.SaveAsync(game);

        var investigator = game.Players.First(p => p.Id == investigatorId);
        if (investigator.ConnectionId != null)
            await _notifier.NotifyLadyResult(investigator.ConnectionId, team.ToString());

        await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());

        await _botService.ProcessBotActionsAsync(gameId);
        return team.ToString();
    }

    public async Task AssassinateAsync(string gameId, string assassinPlayerId, string targetPlayerId)
    {
        var game = await GetGameOrThrow(gameId);
        game.Assassinate(assassinPlayerId, targetPlayerId);
        await _repository.SaveAsync(game);

        await _notifier.NotifyGameOver(gameId, game.Result!.ToString()!);
    }

    public async Task RestartGameAsync(string gameId, string hostPlayerId)
    {
        var game = await GetGameOrThrow(gameId);
        game.Restart(hostPlayerId);
        await _repository.SaveAsync(game);

        await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());
    }

    public async Task<List<JoinGameResponse>> AddBotsAsync(string gameId, string hostPlayerId, int count)
    {
        var game = await GetGameOrThrow(gameId);
        var host = game.Players.FirstOrDefault(p => p.Id == hostPlayerId);
        if (host == null || !host.IsHost)
            throw new InvalidOperationException("Only the host can add bots.");

        var botNames = new[] { "Bot_Alpha", "Bot_Beta", "Bot_Gamma", "Bot_Delta", "Bot_Epsilon", "Bot_Zeta", "Bot_Eta", "Bot_Theta", "Bot_Iota" };
        var results = new List<JoinGameResponse>();

        int added = 0;
        foreach (var name in botNames)
        {
            if (added >= count) break;
            if (game.Players.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            if (game.Players.Count >= GameConfiguration.MaxPlayers) break;

            var bot = game.AddBot(name);
            results.Add(new JoinGameResponse(bot.Id, bot.Id));
            await _notifier.NotifyPlayerJoined(gameId, bot.Name);
            added++;
        }

        await _repository.SaveAsync(game);
        return results;
    }

    private async Task<Game> GetGameOrThrow(string gameId)
    {
        return await _repository.GetByIdAsync(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' not found.");
    }

    private static string GenerateGameId()
    {
        return Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }
}
