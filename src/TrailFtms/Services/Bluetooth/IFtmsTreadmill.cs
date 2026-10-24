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

public enum MachineKind
{
    /// <summary>Bluetooth Fitness Machine Service treadmill.</summary>
    Treadmill,
    /// <summary>
    /// Domyos bike (proprietary protocol): the speed is the user's, the inclination is simulated
    /// by the resistance (<see cref="DomyosProtocol"/>), start and pause do nothing.
    /// </summary>
    Bike,
}

/// <summary>
/// A treadmill driven through the Bluetooth Fitness Machine Service, or a Domyos bike behaving like one
/// (see <see cref="Kind"/>), so that the ride engine and the programs drive both.
/// </summary>
public interface IFtmsTreadmill : IAsyncDisposable
{
    TreadmillState State { get; }
    MachineKind Kind { get; }
    string? DeviceName { get; }
    MachineFeatures? Features { get; }
    SupportedRange? SpeedRange { get; }
    SupportedRange? InclineRange { get; }
    TreadmillData? LastData { get; }

    /// <summary>User ceilings, applied by every speed / incline command.</summary>
    TreadmillLimits Limits { get; set; }

    event Action? StateChanged;
    event Action<TreadmillData>? DataReceived;
    event Action<MachineStatus>? StatusReceived;

    Task<BluetoothAvailability> GetAvailabilityAsync();

    /// <summary>Opens the browser device picker and connects. Returns false if the user cancelled.</summary>
    Task<bool> ConnectAsync();
    /// <summary>Stops the belt (best effort), then disconnects.</summary>
    Task DisconnectAsync();

    // Commands are queued (one in flight) and return the machine's answer.
    // They throw TimeoutException if the machine does not answer, InvalidOperationException if not connected.
    // On a bike, Stop sets the lowest resistance, Start and Pause succeed without sending anything.
    Task<ControlPointResult> RequestControlAsync();
    Task<ControlPointResult> StartAsync();
    Task<ControlPointResult> StopAsync(StopKind kind = StopKind.Stop);

    /// <summary>Sets the target speed, see <see cref="ClampSpeed"/>. Not supported by a bike.</summary>
    Task<ControlPointResult> SetSpeedAsync(double kmh);

    /// <summary>Sets the target inclination, see <see cref="ClampIncline"/>.</summary>
    Task<ControlPointResult> SetInclineAsync(double percent);

    /// <summary>The speed that would actually be sent: user ceiling, then machine range and step.</summary>
    double ClampSpeed(double kmh);

    /// <summary>The inclination that would actually be sent: user ceiling, then machine range and step.</summary>
    double ClampIncline(double percent);
}
