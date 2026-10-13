namespace TrailFtms.Services.Bluetooth;

public enum BluetoothAvailability
{
    /// <summary>The browser has no Web Bluetooth API (Firefox, iOS...).</summary>
    Unsupported,
    /// <summary>Web Bluetooth exists but no adapter is available or it is turned off.</summary>
    Unavailable,
    Available,
}

public enum TreadmillState
{
    Disconnected,
    Connecting,
    Connected,
    /// <summary>The link dropped without the user asking for it.</summary>
    ConnectionLost,
}

/// <summary>A treadmill driven through the Bluetooth Fitness Machine Service.</summary>
public interface IFtmsTreadmill : IAsyncDisposable
{
    TreadmillState State { get; }
    string? DeviceName { get; }
    MachineFeatures? Features { get; }
    SupportedRange? SpeedRange { get; }
    SupportedRange? InclineRange { get; }
    TreadmillData? LastData { get; }

    event Action? StateChanged;
    event Action<TreadmillData>? DataReceived;
    event Action<MachineStatus>? StatusReceived;

    Task<BluetoothAvailability> GetAvailabilityAsync();

    /// <summary>Opens the browser device picker and connects. Returns false if the user cancelled.</summary>
    Task<bool> ConnectAsync();
    Task DisconnectAsync();

    // Commands are queued (one in flight) and return the machine's answer.
    // They throw TimeoutException if the machine does not answer, InvalidOperationException if not connected.
    Task<ControlPointResult> RequestControlAsync();
    Task<ControlPointResult> StartAsync();
    Task<ControlPointResult> StopAsync(StopKind kind = StopKind.Stop);

    /// <summary>Sets the target speed, clamped and snapped to the machine range.</summary>
    Task<ControlPointResult> SetSpeedAsync(double kmh);

    /// <summary>Sets the target inclination, clamped and snapped to the machine range.</summary>
    Task<ControlPointResult> SetInclineAsync(double percent);
}
