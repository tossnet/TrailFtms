namespace TrailFtms.Services.Bluetooth;

/// <summary>
/// Range supported by the machine for a target value (speed in km/h or inclination in %),
/// as read from 0x2AD4 / 0x2AD5.
/// </summary>
public readonly record struct SupportedRange(double Min, double Max, double Step)
{
    /// <summary>Clamps <paramref name="value"/> to [Min, Max] and snaps it to the nearest step from Min.</summary>
    public double Clamp(double value)
    {
        if (double.IsNaN(value))
            return Min;

        var clamped = Math.Clamp(value, Min, Max);
        if (Step <= 0)
            return clamped;

        // Pre-round the step count so that floating point noise (4.05 / 0.1 = 40.4999...) does not flip the midpoint.
        var steps = Math.Round(Math.Round((clamped - Min) / Step, 9), MidpointRounding.AwayFromZero);
        var snapped = Min + steps * Step;

        // Snapping may overshoot Max when (Max - Min) is not a multiple of Step.
        if (snapped > Max)
            snapped -= Step;

        // Values travel with 0.01 resolution at most: drop floating point noise.
        return Math.Round(snapped, 2);
    }
}
