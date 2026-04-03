using Avalon.Domain.Enums;

namespace Avalon.Domain.Models;

public class Player
{
    public string Id { get; }
    public string Name { get; private set; }
    public Role? Role { get; internal set; }
    public Team? Team { get; internal set; }
    public bool IsHost { get; }
    public string? ConnectionId { get; set; }

    public Player(string id, string name, bool isHost)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IsHost = isHost;
    }
}
