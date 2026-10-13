namespace TrailFtms.Services.Ride;

/// <summary>What the treadmill does while the video is paused.</summary>
public enum PauseBehavior
{
    /// <summary>Keep the current targets.</summary>
    Nothing,
    /// <summary>Slow down to <see cref="RideSettings.PauseSpeed"/>, then resume the program.</summary>
    SlowDown,
    /// <summary>Pause the belt, start it again when the video resumes.</summary>
    PauseTreadmill,
}

public sealed record RideSettings
{
    /// <summary>Applied to every program speed (0.9 = 90 %).</summary>
    public double SpeedFactor { get; init; } = 1.0;

    public PauseBehavior PauseBehavior { get; init; } = PauseBehavior.SlowDown;

    /// <summary>Walking speed used by <see cref="PauseBehavior.SlowDown"/>, in km/h.</summary>
    public double PauseSpeed { get; init; } = 3.0;
}
