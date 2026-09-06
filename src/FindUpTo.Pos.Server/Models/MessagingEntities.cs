namespace FindUpTo.Pos.Server.Models;

public sealed class Conversation
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Participants { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Message
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public string SenderUsername { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
