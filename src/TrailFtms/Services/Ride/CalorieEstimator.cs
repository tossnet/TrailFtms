using TrailFtms.Services.Bluetooth;

namespace TrailFtms.Services.Ride;

/// <summary>
/// Estimates the energy burnt from the treadmill measurements: ACSM walking and running equations
/// (speed, incline, weight), or Keytel et al. 2005 from the heart rate when the sex and age are known.
/// About 15 to 20 % off at best, like the treadmill console.
/// </summary>
public sealed class CalorieEstimator
{
    /// <summary>ACSM: walking equation up to this speed, running equation above.</summary>
    private const double RunningFromKmh = 7;

    /// <summary>A longer silence (lost notifications, disconnection) is not counted beyond this.</summary>
    private const double MaxGapSeconds = 5;

    private double? _lastSeconds;
    private double _kcalPerSecond;

    /// <summary>Null while the user has not given a profile: nothing is counted.</summary>
    public BodyProfile? Profile { get; set; }

    public double TotalKcal { get; private set; }

    /// <summary>Feeds a measure taken at <paramref name="seconds"/> (monotonic clock): the previous rate applies until now.</summary>
    public void Observe(double seconds, TreadmillData data)
    {
        if (_lastSeconds is { } last)
            TotalKcal += _kcalPerSecond * Math.Clamp(seconds - last, 0, MaxGapSeconds);
        _lastSeconds = seconds;
        _kcalPerSecond = Profile is null ? 0 : KcalPerMinute(Profile, data.SpeedKmh ?? 0, data.InclinePercent ?? 0, data.HeartRateBpm) / 60;
    }

    /// <summary>Energy rate in kcal/min, 0 when the belt is stopped.</summary>
    public static double KcalPerMinute(BodyProfile profile, double speedKmh, double inclinePercent, int? heartRate)
    {
        if (speedKmh <= 0 || profile.WeightKg <= 0)
            return 0;

        if (heartRate is > 0 and var hr && profile.Sex != Sex.Unspecified)
        {
            // Keytel: kJ/min, valid for an exercising heart rate.
            var kj = profile.Sex == Sex.Male
                ? -55.0969 + 0.6309 * hr + 0.1988 * profile.WeightKg + 0.2017 * profile.Age
                : -20.4022 + 0.4472 * hr - 0.1263 * profile.WeightKg + 0.074 * profile.Age;
            return Math.Max(0, kj / 4.184);
        }

        // ACSM: VO2 in ml/kg/min, speed in m/min, grade as a fraction (no gain downhill). About 5 kcal per litre of O2.
        var metersPerMinute = speedKmh * 1000 / 60;
        var grade = Math.Max(0, inclinePercent) / 100;
        var vo2 = speedKmh < RunningFromKmh
            ? 3.5 + 0.1 * metersPerMinute + 1.8 * metersPerMinute * grade
            : 3.5 + 0.2 * metersPerMinute + 0.9 * metersPerMinute * grade;
        return vo2 * profile.WeightKg / 1000 * 5;
    }
}
