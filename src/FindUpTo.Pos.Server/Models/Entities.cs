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
    public string BusinessName { get; set; } = "MK Pizza & Ice Bar";
    public string Phone { get; set; } = "03169700025";
    public string Address { get; set; } = "Abbas Chowk Collage Road Bhakkar";
    public decimal TaxPercent { get; set; } = 0;
    public string CurrencyCode { get; set; } = "PKR";
    public string CurrencySymbol { get; set; } = "Rs.";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(string Token, int UserId, string Username, string Role);
