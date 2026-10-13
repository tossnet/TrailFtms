namespace TrailFtms.Services.Bluetooth;

/// <summary>Fitness Machine Status (0x2ADA) op codes: state changes initiated by the machine.</summary>
public enum MachineStatus : byte
{
    Reset = 0x01,
    StoppedOrPausedByUser = 0x02,
    StoppedBySafetyKey = 0x03,
    StartedOrResumedByUser = 0x04,
    TargetSpeedChanged = 0x05,
    TargetInclineChanged = 0x06,
    ControlPermissionLost = 0xFF,
}
