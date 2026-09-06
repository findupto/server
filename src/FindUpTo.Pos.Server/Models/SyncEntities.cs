namespace FindUpTo.Pos.Server.Models;

public sealed class SyncOperation
{
    public long Id { get; set; }
    public string ClientOperationId { get; set; } = "";
    public int OrderId { get; set; }
    public string CreatedByUsername { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
