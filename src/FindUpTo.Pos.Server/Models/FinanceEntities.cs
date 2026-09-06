namespace FindUpTo.Pos.Server.Models;

public sealed class Expense
{
    public int Id { get; set; }
    public string Category { get; set; } = "General";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime ExpenseDateUtc { get; set; } = DateTime.UtcNow;
    public string PaymentMethod { get; set; } = "Cash";
    public string Reference { get; set; } = "";
    public string CreatedByUsername { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record ExpenseRequest(string Category, string Description, decimal Amount, DateTime? ExpenseDateUtc = null, string PaymentMethod = "Cash", string Reference = "");
