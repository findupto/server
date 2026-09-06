namespace FindUpTo.Pos.Server.Models;

public sealed class SyncConflict
{
    public long Id { get; set; }
    public string ClientOperationId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Details { get; set; } = "";
    public string Username { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
