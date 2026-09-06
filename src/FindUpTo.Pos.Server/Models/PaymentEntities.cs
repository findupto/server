namespace FindUpTo.Pos.Server.Models;

public sealed class Payment
{
    public int Id { get; set; }
    public int PosOrderId { get; set; }
    public string Method { get; set; } = "Cash";
    public decimal Amount { get; set; }
    public decimal ReceivedAmount { get; set; }
    public decimal ChangeAmount { get; set; }
    public string Status { get; set; } = "Paid";
    public string CollectedByUsername { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record CreatePaymentRequest(string Method, decimal ReceivedAmount);
