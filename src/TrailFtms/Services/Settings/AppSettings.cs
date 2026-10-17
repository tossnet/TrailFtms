using TrailFtms.Services.Ride;

namespace TrailFtms.Services.Settings;

/// <summary>User preferences, kept in the browser.</summary>
public sealed record AppSettings
{
    public RideSettings Ride { get; init; } = new();

    /// <summary>Speed ceiling in km/h, null for the machine limit only.</summary>
    public double? MaxSpeed { get; init; }

    /// <summary>Incline ceiling in %, null for the machine limit only.</summary>
    public double? MaxIncline { get; init; }

    /// <summary>Profile for the calorie estimate, null to show the treadmill value only.</summary>
    public BodyProfile? Body { get; init; }
}
