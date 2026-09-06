namespace FindUpTo.Pos.Server.Models;

public sealed class CustomerCreditAccount
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal Balance { get; set; }
    public bool Active { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CreditTransaction
{
    public int Id { get; set; }
    public int CustomerCreditAccountId { get; set; }
    public int? PosOrderId { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = "Charge";
    public string Reference { get; set; } = "";
    public string Notes { get; set; } = "";
    public string CreatedByUsername { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record CreditAccountRequest(decimal CreditLimit, bool Active = true);
public sealed record CreditPaymentRequest(decimal Amount, string Reference = "", string Notes = "");
