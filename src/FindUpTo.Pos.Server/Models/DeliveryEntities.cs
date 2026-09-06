namespace FindUpTo.Pos.Server.Models;

public sealed class RiderProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string VehicleType { get; set; } = "Motorcycle";
    public string VehicleNumber { get; set; } = "";
    public bool Online { get; set; }
    public bool Available { get; set; } = true;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RiderLocation
{
    public long Id { get; set; }
    public int RiderId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public double? SpeedMetersPerSecond { get; set; }
    public double? HeadingDegrees { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DeliveryTracking
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int? RiderId { get; set; }
    public string TrackingCode { get; set; } = "";
    public string Status { get; set; } = "Preparing";
    public string DeliveryAddress { get; set; } = "";
    public double? DestinationLatitude { get; set; }
    public double? DestinationLongitude { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record RiderLocationRequest(double Latitude, double Longitude, double? AccuracyMeters = null, double? SpeedMetersPerSecond = null, double? HeadingDegrees = null);
public sealed record RiderAvailabilityRequest(bool Online, bool Available = true);
public sealed record AssignRiderRequest(int RiderId);
public sealed record DeliveryTrackingRequest(string Status, string DeliveryAddress = "", double? DestinationLatitude = null, double? DestinationLongitude = null);
public sealed record RiderCreateRequest(int UserId, string VehicleType = "Motorcycle", string VehicleNumber = "");