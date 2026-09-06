namespace FindUpTo.Pos.Server.Models;

public sealed class CashDrawerSession
{
    public long Id { get; set; }
    public string OpenedByUsername { get; set; } = "";
    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal OpeningFloat { get; set; }
    public decimal ClosingAmount { get; set; }
    public string Status { get; set; } = "Open";
    public string ClosedByUsername { get; set; } = "";
    public DateTime? ClosedAtUtc { get; set; }
    public string Notes { get; set; } = "";
    public List<CashDrawerMovement> Movements { get; set; } = [];
}

public sealed class CashDrawerMovement
{
    public long Id { get; set; }
    public long CashDrawerSessionId { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = "Adjustment";
    public string Reason { get; set; } = "";
    public string Username { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record OpenCashDrawerRequest(decimal OpeningFloat = 0m, string Notes = "");
public sealed record CashDrawerMovementRequest(decimal Amount, string Type, string Reason = "");
public sealed record CloseCashDrawerRequest(decimal ClosingAmount, string Notes = "");
