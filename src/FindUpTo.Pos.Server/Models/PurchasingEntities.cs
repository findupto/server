namespace FindUpTo.Pos.Server.Models;

public sealed class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PurchaseOrder
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public string Status { get; set; } = "Draft";
    public string CreatedByUsername { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<PurchaseOrderItem> Items { get; set; } = [];
}

public sealed class PurchaseOrderItem
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal QuantityOrdered { get; set; }
    public decimal QuantityReceived { get; set; }
    public decimal UnitCost { get; set; }
}

public sealed record SupplierRequest(string Name, string Phone = "", string Email = "", string Address = "", string Notes = "", bool Active = true);
public sealed record PurchaseOrderItemRequest(int ProductId, decimal QuantityOrdered, decimal UnitCost = 0);
public sealed record PurchaseOrderRequest(int SupplierId, List<PurchaseOrderItemRequest> Items, string Notes = "");
public sealed record PurchaseReceiveItemRequest(int ProductId, decimal Quantity);
public sealed record PurchaseReceiveRequest(List<PurchaseReceiveItemRequest> Items, string Notes = "");
