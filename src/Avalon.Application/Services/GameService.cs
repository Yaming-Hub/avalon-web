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
    private readonly IActivityLog _log;
    private readonly IConversationService _chat;

    public GameService(IGameRepository repository, IGameNotifier notifier, GameStateMapper mapper, BotService botService, IActivityLog log, IConversationService chat)
    {
        _repository = repository;
        _notifier = notifier;
        _mapper = mapper;
        _botService = botService;
        _log = log;
        _chat = chat;
    }

    public async Task<CreateGameResponse> CreateGameAsync(string hostName)
    {
        var gameId = GenerateGameId();
        var game = new Game(gameId);
        var host = game.Join(hostName);

        await _repository.SaveAsync(game);
        _log.Log(gameId, "Game", $"Game created by '{hostName}'", new { hostId = host.Id });
        return new CreateGameResponse(gameId, host.Id, host.Id, $"/game/{gameId}");
    }

    public async Task<JoinGameResponse> JoinGameAsync(string gameId, string playerName)
    {
        var game = await GetGameOrThrow(gameId);
        var playerCountBefore = game.Players.Count;
        var observerCountBefore = game.Observers.Count;
        var player = game.Join(playerName);
        await _repository.SaveAsync(game);

        if (game.Players.Count > playerCountBefore)
        {
            _log.Log(gameId, "Player", $"'{playerName}' joined (new)", new { playerId = player.Id });
            _chat.PostMessage(gameId, "System", $"{playerName} joined the game.", isSystem: true);
            await _notifier.NotifyPlayerJoined(gameId, playerName);
        }
        else if (game.Observers.Count > observerCountBefore)
        {
            _log.Log(gameId, "Player", $"'{playerName}' joined as observer", new { playerId = player.Id, phase = game.Phase.ToString() });
            _chat.PostMessage(gameId, "System", $"👁️ {playerName} joined as an observer.", isSystem: true);
        }
        else
        {
            _log.Log(gameId, "Player", $"'{playerName}' re-joined", new { playerId = player.Id, phase = game.Phase.ToString() });
        }

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
            ActivityLogEnabled = request.ActivityLogEnabled,
        };
        game.UpdateSettings(hostPlayerId, settings);
        await _repository.SaveAsync(game);
        _log.Log(gameId, "Settings", "Settings updated");
    }

    public async Task StartGameAsync(string gameId, string hostPlayerId)
    {
        var game = await GetGameOrThrow(gameId);

        // Enable/disable activity logging based on game settings
        if (game.Settings.ActivityLogEnabled)
            _log.EnableLogging(gameId);
        else
            _log.DisableLogging(gameId);

        _log.Log(gameId, "Game", $"Starting game with {game.Players.Count} players", new { players = game.Players.Select(p => new { p.Id, p.Name, p.IsBot }).ToList() });
        game.Start(hostPlayerId);
        game.ProceedFromRoleReveal();
        await _repository.SaveAsync(game);

        _log.Log(gameId, "Phase", $"Game started → {game.Phase}", new { leader = game.CurrentLeader?.Name, round = game.CurrentRound?.RoundNumber });
        _log.Log(gameId, "Roles", "Roles assigned", new { roles = game.Players.Select(p => new { p.Name, Role = p.Role?.ToString(), Team = p.Team?.ToString() }).ToList() });

        _chat.PostMessage(gameId, "System", $"🎮 Game started! Round 1 begins. {game.CurrentLeader?.Name} is the first leader.", isSystem: true);

        await _notifier.NotifyGameStarted(gameId);
        await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());

        await _botService.ProcessBotActionsAsync(gameId);
    }

    public async Task ProposeTeamAsync(string gameId, string playerId, List<string> proposedPlayerIds)
    {
        var game = await GetGameOrThrow(gameId);
        var leaderName = game.Players.First(p => p.Id == playerId).Name;
        _log.Log(gameId, "Action", $"'{leaderName}' proposing team", new { playerId, proposedPlayerIds, currentPhase = game.Phase.ToString() });

        game.ProposeTeam(playerId, proposedPlayerIds);
        await _repository.SaveAsync(game);

        var proposedNames = proposedPlayerIds.Select(id => game.Players.First(p => p.Id == id).Name).ToList();
        _log.Log(gameId, "Phase", $"Team proposed → {game.Phase}", new { leader = leaderName, team = proposedNames });

        _chat.PostMessage(gameId, leaderName, $"proposed team: {string.Join(", ", proposedNames)}");


        await _notifier.NotifyTeamProposed(gameId, leaderName, proposedNames);
        await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());

        await _botService.ProcessBotActionsAsync(gameId);
    }

    public async Task VoteOnProposalAsync(string gameId, string playerId, VoteType vote)
    {
        var game = await GetGameOrThrow(gameId);
        var playerName = game.Players.FirstOrDefault(p => p.Id == playerId)?.Name ?? playerId;
        var previousPhase = game.Phase;
        _log.Log(gameId, "Action", $"'{playerName}' voting {vote} on proposal", new { playerId, vote = vote.ToString(), currentPhase = game.Phase.ToString() });

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
                _log.Log(gameId, "Phase", $"Proposal {(proposal.IsApproved.Value ? "APPROVED" : "REJECTED")} → {game.Phase}", new { votes, consecutiveRejections = game.ConsecutiveRejections });
                await _notifier.NotifyVoteRevealed(gameId, votes);

                // Post vote results to conversation
                var approvers = votes.Where(kv => kv.Value == "Approve").Select(kv => kv.Key).ToList();
                var rejecters = votes.Where(kv => kv.Value == "Reject").Select(kv => kv.Key).ToList();
                var resultText = proposal.IsApproved.Value ? "✅ Team APPROVED" : "❌ Team REJECTED";
                var voteDetail = "";
                if (approvers.Count > 0) voteDetail += $"Approve: {string.Join(", ", approvers)}";
                if (rejecters.Count > 0) voteDetail += (voteDetail.Length > 0 ? " | " : "") + $"Reject: {string.Join(", ", rejecters)}";
                _chat.PostMessage(gameId, "System", $"{resultText} ({approvers.Count}👍 {rejecters.Count}👎). {voteDetail}", isSystem: true);

                if (!proposal.IsApproved.Value && game.ConsecutiveRejections > 0)
                    _chat.PostMessage(gameId, "System", $"⚠️ {game.ConsecutiveRejections}/5 consecutive rejections.", isSystem: true);
            }

            if (game.Phase == GamePhase.GameOver)
                _chat.PostMessage(gameId, "System", "💀 5 consecutive rejections! EVIL TEAM WINS!", isSystem: true);

            await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());
        }

        await _botService.ProcessBotActionsAsync(gameId);
    }

    public async Task VoteOnQuestAsync(string gameId, string playerId, QuestVote vote)
    {
        var game = await GetGameOrThrow(gameId);
        var playerName = game.Players.FirstOrDefault(p => p.Id == playerId)?.Name ?? playerId;
        var previousPhase = game.Phase;
        _log.Log(gameId, "Action", $"'{playerName}' quest-voting {vote}", new { playerId, vote = vote.ToString(), currentPhase = game.Phase.ToString() });

        game.VoteOnQuest(playerId, vote);
        await _repository.SaveAsync(game);

        if (game.Phase != previousPhase)
        {
            var quest = game.CurrentRound!.Quest!;
            _log.Log(gameId, "Phase", $"Quest resolved ({quest.SuccessCount}✓ {quest.FailCount}✗) → {game.Phase}", new { success = quest.IsSuccess });

            var questResult = quest.IsSuccess!.Value
                ? $"✅ Quest PASSED ({quest.SuccessCount} success, {quest.FailCount} fail)"
                : $"❌ Quest FAILED ({quest.SuccessCount} success, {quest.FailCount} fail)";
            _chat.PostMessage(gameId, "System", questResult, isSystem: true);
            _chat.PostMessage(gameId, "System", $"Score: Good {game.CompletedQuestsGood} — Evil {game.CompletedQuestsEvil}", isSystem: true);

            await _notifier.NotifyQuestResult(gameId, quest.SuccessCount, quest.FailCount);
            await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());
        }

        await _botService.ProcessBotActionsAsync(gameId);
    }

    public async Task ProceedFromQuestResultAsync(string gameId)
    {
        var game = await GetGameOrThrow(gameId);
        _log.Log(gameId, "Action", "Proceed from quest result requested", new { currentPhase = game.Phase.ToString(), round = game.CurrentRound?.RoundNumber });

        game.ProceedFromQuestResult();
        await _repository.SaveAsync(game);

        _log.Log(gameId, "Phase", $"Proceeded → {game.Phase}", new { goodWins = game.CompletedQuestsGood, evilWins = game.CompletedQuestsEvil });

        if (game.Phase == GamePhase.TeamProposal)
            _chat.PostMessage(gameId, "System", $"📋 Round {game.CurrentRound?.RoundNumber} begins. {game.CurrentLeader?.Name} is the leader. Team size: {game.CurrentRound?.RequiredTeamSize}", isSystem: true);
        else if (game.Phase == GamePhase.AssassinVote)
            _chat.PostMessage(gameId, "System", "⚔️ Good has won 3 quests! But the Assassin now has a chance to identify Merlin...", isSystem: true);
        else if (game.Phase == GamePhase.GameOver)
        {
            var resultMsg = game.Result switch
            {
                GameResult.GoodWins => "🎉 GOOD TEAM WINS! The forces of good have triumphed!",
                GameResult.EvilWins => "💀 EVIL TEAM WINS! The forces of darkness prevail!",
                GameResult.EvilWinsByAssassination => "🗡️ EVIL TEAM WINS by assassination! Merlin has been found!",
                _ => "Game over!"
            };
            _chat.PostMessage(gameId, "System", resultMsg, isSystem: true);
        }

        await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());

        if (game.Phase == GamePhase.GameOver)
            await _notifier.NotifyGameOver(gameId, game.Result!.ToString()!);

        await _botService.ProcessBotActionsAsync(gameId);
    }

    public async Task<string> InvestigateWithLadyAsync(string gameId, string investigatorId, string targetId)
    {
        var game = await GetGameOrThrow(gameId);
        _log.Log(gameId, "Action", "Lady of the Lake investigation", new { investigatorId, targetId });

        var team = game.InvestigateWithLady(investigatorId, targetId);
        await _repository.SaveAsync(game);

        _log.Log(gameId, "Phase", $"Lady investigation complete → {game.Phase}", new { result = team.ToString() });

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
        var targetName = game.Players.First(p => p.Id == targetPlayerId).Name;
        _log.Log(gameId, "Action", "Assassination attempt", new { assassinPlayerId, targetPlayerId });

        game.Assassinate(assassinPlayerId, targetPlayerId);
        await _repository.SaveAsync(game);

        _log.Log(gameId, "Phase", $"Game over: {game.Result}", new { target = game.AssassinTargetId });

        var resultMsg = game.Result switch
        {
            GameResult.EvilWinsByAssassination => $"🗡️ The Assassin targeted {targetName} — it was Merlin! EVIL TEAM WINS by assassination!",
            GameResult.GoodWins => $"🗡️ The Assassin targeted {targetName} — wrong guess! GOOD TEAM WINS!",
            _ => "Game over!"
        };
        _chat.PostMessage(gameId, "System", resultMsg, isSystem: true);

        await _notifier.NotifyGameOver(gameId, game.Result!.ToString()!);
    }

    public async Task RestartGameAsync(string gameId, string hostPlayerId)
    {
        var game = await GetGameOrThrow(gameId);
        game.Restart(hostPlayerId);
        await _repository.SaveAsync(game);

        // Add visual separator in chat between games
        _chat.PostMessage(gameId, "System", "─────────────────────────────", isSystem: true);
        _chat.PostMessage(gameId, "System", "", isSystem: true);
        _chat.PostMessage(gameId, "System", "", isSystem: true);
        _chat.PostMessage(gameId, "System", "🔄 New game started with same players!", isSystem: true);

        await _notifier.NotifyPhaseChanged(gameId, game.Phase.ToString());
    }

    public async Task MarkLogsAccessedAsync(string gameId)
    {
        var game = await GetGameOrThrow(gameId);
        if (!game.LogsAccessed)
        {
            game.MarkLogsAccessed();
            await _repository.SaveAsync(game);
            _log.Log(gameId, "Security", "⚠️ Activity logs accessed — game integrity compromised");
        }
    }

    public async Task PostChatMessageAsync(string gameId, string playerId, string message)
    {
        var game = await GetGameOrThrow(gameId);
        var player = game.Players.FirstOrDefault(p => p.Id == playerId)
            ?? throw new InvalidOperationException("Player not found in game.");
        _chat.PostMessage(gameId, player.Name, message);
    }

    public List<ConversationMessage> GetChatMessages(string gameId, int? since = null)
    {
        return _chat.GetMessages(gameId, since);
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
