namespace FindUpTo.Pos.Server.Models;

public sealed class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BusinessSetting
{
    public int Id { get; set; }
    public string BusinessName { get; set; } = "FindUpTo POS";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public decimal TaxPercent { get; set; }
    public string CurrencyCode { get; set; } = "PKR";
    public string CurrencySymbol { get; set; } = "Rs.";
    public string AiProvider { get; set; } = "auto";
    public string AiModel { get; set; } = "";
    public string AiBaseUrl { get; set; } = "";
    public string AiApiKeyEncrypted { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(string Token, int UserId, string Username, string Role);
