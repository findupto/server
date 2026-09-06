namespace FindUpTo.Pos.Server.Models;

public sealed class ProductInventory
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal ReorderLevel { get; set; }
    public bool TrackInventory { get; set; } = false;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class StockMovement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public decimal QuantityChange { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Type { get; set; } = "Adjustment";
    public string Reason { get; set; } = "";
    public string Username { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record InventoryUpdateRequest(decimal QuantityOnHand, decimal ReorderLevel = 0, bool TrackInventory = true, string Reason = "");
public sealed record StockAdjustmentRequest(decimal QuantityChange, string Reason = "");
