namespace FindUpTo.Pos.Server.Models;

public sealed class RelayDevice
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SecretHash { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAtUtc { get; set; }
}

public sealed record CreateRelayDeviceRequest(string Name);
public sealed record RelayDeviceLoginRequest(int DeviceId, string Secret);
public sealed record RelayMessageRequest(int TargetDeviceId, string Type, string Payload);
