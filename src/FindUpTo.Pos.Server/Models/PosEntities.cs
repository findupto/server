namespace FindUpTo.Pos.Server.Models;

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class Product
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = "";
    public bool Available { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PosOrder
{
    public int Id { get; set; }
    public int? CustomerId { get; set; }
    public string CreatedByUsername { get; set; } = "";
    public string OrderType { get; set; } = "Counter";
    public string Status { get; set; } = "New";
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string Notes { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<OrderItem> Items { get; set; } = [];
}

public sealed class OrderItem
{
    public int Id { get; set; }
    public int PosOrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string Notes { get; set; } = "";
    public decimal LineTotal { get; set; }
}

public sealed class Payment
{
    public int Id { get; set; }
    public int PosOrderId { get; set; }
    public decimal AmountTendered { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal ChangeAmount { get; set; }
    public string Method { get; set; } = "Cash";
    public string Status { get; set; } = "Paid";
    public string Reference { get; set; } = "";
    public string CollectedByUsername { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record CategoryRequest(string Name, int SortOrder = 0);
public sealed record ProductRequest(int CategoryId, string Name, string Description, decimal Price, string ImageUrl, bool Available = true);
public sealed record CustomerRequest(string Name, string Phone, string Address, string Notes);
public sealed record OrderItemRequest(int ProductId, int Quantity, string Notes = "");
public sealed record CreateOrderRequest(int? CustomerId, string OrderType, List<OrderItemRequest> Items, string Notes = "");
public sealed record UpdateOrderStatusRequest(string Status);
public sealed record CollectPaymentRequest(decimal AmountTendered, string Method = "Cash", string Reference = "");
