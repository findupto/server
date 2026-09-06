namespace FindUpTo.Pos.Server.Models;

public sealed class PushDevice
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Token { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class NotificationMessage
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string DataJson { get; set; } = "{}";
    public string Status { get; set; } = "Queued";
    public int Attempts { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public string LastError { get; set; } = "";
}
