namespace Avalon.Application.Interfaces;

/// <summary>
/// Records game activity for debugging purposes.
/// Stores actions, decisions, phase transitions, and errors.
/// Only records when logging is enabled for a game.
/// </summary>
public interface IActivityLog
{
    void EnableLogging(string gameId);
    void DisableLogging(string gameId);
    bool IsEnabled(string gameId);
    void Log(string gameId, string category, string message, object? details = null);
    List<ActivityLogEntry> GetLogs(string gameId, int? limit = null);
    void Clear(string gameId);
}

public class ActivityLogEntry
{
    public DateTime Timestamp { get; set; }
    public string GameId { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string Message { get; set; } = default!;
    public object? Details { get; set; }
}
