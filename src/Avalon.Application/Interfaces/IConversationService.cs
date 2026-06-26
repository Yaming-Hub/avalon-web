namespace Avalon.Application.Interfaces;

public interface IConversationService
{
    void PostMessage(string gameId, string sender, string message, bool isSystem = false);
    List<ConversationMessage> GetMessages(string gameId, int? since = null);
    int GetMessageCount(string gameId);
}

public class ConversationMessage
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string GameId { get; set; } = default!;
    public string Sender { get; set; } = default!;
    public string Message { get; set; } = default!;
    public bool IsSystem { get; set; }
}
