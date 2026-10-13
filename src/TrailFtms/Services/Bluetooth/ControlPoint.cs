namespace TrailFtms.Services.Bluetooth;

/// <summary>Fitness Machine Control Point (0x2AD9) request op codes.</summary>
public enum ControlPointOpCode : byte
{
    RequestControl = 0x00,
    Reset = 0x01,
    SetTargetSpeed = 0x02,
    SetTargetInclination = 0x03,
    StartOrResume = 0x07,
    StopOrPause = 0x08,
    ResponseCode = 0x80,
}

/// <summary>Result code carried by a Control Point response indication.</summary>
public enum ControlPointResult : byte
{
    Success = 0x01,
    NotSupported = 0x02,
    InvalidParameter = 0x03,
    OperationFailed = 0x04,
    ControlNotPermitted = 0x05,
}

/// <summary>Parameter of the Stop or Pause command.</summary>
public enum StopKind : byte
{
    Stop = 0x01,
    Pause = 0x02,
}

/// <summary>Decoded Control Point response: which request it answers and its result.</summary>
public readonly record struct ControlPointResponse(ControlPointOpCode RequestOpCode, ControlPointResult Result)
{
    public bool IsSuccess => Result == ControlPointResult.Success;
}
