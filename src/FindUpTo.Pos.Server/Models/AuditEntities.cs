namespace FindUpTo.Pos.Server.Models;

public sealed class AuditLog
{
    public long Id { get; set; }
    public string Username { get; set; } = "system";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
