namespace TrailFtms.Services.Bluetooth;

/// <summary>Fitness Machine Features field of 0x2ACC.</summary>
[Flags]
public enum FitnessMachineFeatures : uint
{
    None = 0,
    AverageSpeed = 1 << 0,
    Cadence = 1 << 1,
    TotalDistance = 1 << 2,
    Inclination = 1 << 3,
    ElevationGain = 1 << 4,
    Pace = 1 << 5,
    StepCount = 1 << 6,
    ResistanceLevel = 1 << 7,
    StrideCount = 1 << 8,
    ExpendedEnergy = 1 << 9,
    HeartRateMeasurement = 1 << 10,
    MetabolicEquivalent = 1 << 11,
    ElapsedTime = 1 << 12,
    RemainingTime = 1 << 13,
    PowerMeasurement = 1 << 14,
    ForceOnBeltAndPowerOutput = 1 << 15,
    UserDataRetention = 1 << 16,
}

/// <summary>Target Setting Features field of 0x2ACC.</summary>
[Flags]
public enum TargetSettingFeatures : uint
{
    None = 0,
    SpeedTarget = 1 << 0,
    InclinationTarget = 1 << 1,
    ResistanceTarget = 1 << 2,
    PowerTarget = 1 << 3,
    HeartRateTarget = 1 << 4,
}

/// <summary>Decoded Fitness Machine Feature characteristic (0x2ACC).</summary>
public readonly record struct MachineFeatures(FitnessMachineFeatures Machine, TargetSettingFeatures TargetSetting)
{
    public bool CanSetSpeed => TargetSetting.HasFlag(TargetSettingFeatures.SpeedTarget);
    public bool CanSetInclination => TargetSetting.HasFlag(TargetSettingFeatures.InclinationTarget);
}
