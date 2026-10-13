namespace TrailFtms.Services.Bluetooth;

/// <summary>
/// Live measurements decoded from a Treadmill Data notification (0x2ACD).
/// A null field means the machine did not send it (or flagged it as unavailable).
/// </summary>
public sealed record TreadmillData
{
    public double? SpeedKmh { get; init; }
    public double? AverageSpeedKmh { get; init; }
    public int? TotalDistanceMeters { get; init; }
    public double? InclinePercent { get; init; }
    public int? TotalEnergyKcal { get; init; }
    public int? HeartRateBpm { get; init; }
    public TimeSpan? ElapsedTime { get; init; }
    public TimeSpan? RemainingTime { get; init; }
}
