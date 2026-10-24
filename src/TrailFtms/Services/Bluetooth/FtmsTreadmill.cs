using Microsoft.JSInterop;

namespace TrailFtms.Services.Bluetooth;

/// <summary>FTMS treadmill, or Domyos bike (<see cref="DomyosProtocol"/>), over Web Bluetooth (wwwroot/js/ftms.js).</summary>
public sealed class FtmsTreadmill(IJSRuntime js) : IFtmsTreadmill
{
    private const ushort FeatureUuid = 0x2ACC;
    private const ushort SpeedRangeUuid = 0x2AD4;
    private const ushort InclineRangeUuid = 0x2AD5;

    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private IJSObjectReference? _module;
    private DotNetObjectReference<FtmsTreadmill>? _selfRef;
    private TaskCompletionSource<ControlPointResult>? _pending;
    private ControlPointOpCode _pendingOpCode;
    private bool _disconnectRequested;
    private readonly DomyosFrameAssembler _domyosFrames = new();
    private CancellationTokenSource? _polling;

    /// <summary>How long to wait for the Control Point answer before giving up.</summary>
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How often the bike measurements are requested. Zero: never (tests feed them by hand).</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(300);

    public TreadmillState State { get; private set; }
    public MachineKind Kind { get; private set; }
    public string? DeviceName { get; private set; }
    public MachineFeatures? Features { get; private set; }
    public SupportedRange? SpeedRange { get; private set; }
    public SupportedRange? InclineRange { get; private set; }
    public TreadmillData? LastData { get; private set; }
    public TreadmillLimits Limits { get; set; } = new();

    public event Action? StateChanged;
    public event Action<TreadmillData>? DataReceived;
    public event Action<MachineStatus>? StatusReceived;

    public async Task<BluetoothAvailability> GetAvailabilityAsync()
    {
        var module = await GetModuleAsync();
        return await module.InvokeAsync<string>("getAvailability") switch
        {
            "available" => BluetoothAvailability.Available,
            "unavailable" => BluetoothAvailability.Unavailable,
            _ => BluetoothAvailability.Unsupported,
        };
    }

    public async Task<bool> ConnectAsync()
    {
        if (State is TreadmillState.Connecting or TreadmillState.Connected)
            return State == TreadmillState.Connected;

        SetState(TreadmillState.Connecting);
        var module = await GetModuleAsync();
        _selfRef ??= DotNetObjectReference.Create(this);
        _disconnectRequested = false;
        StopPolling();
        try
        {
            var device = await module.InvokeAsync<ConnectedDevice?>("connect", _selfRef);
            if (device is null)
            {
                SetState(TreadmillState.Disconnected);
                return false;
            }

            DeviceName = device.Name;
            Kind = device.Protocol == ConnectedDevice.Domyos ? MachineKind.Bike : MachineKind.Treadmill;
            LastData = null;
            if (Kind == MachineKind.Bike)
            {
                Features = null;
                SpeedRange = null;
                InclineRange = DomyosProtocol.SimulatedInclineRange;
                _domyosFrames.Reset();
                SetState(TreadmillState.Connected);
                await StartBikeAsync();
                return true;
            }

            Features = FtmsProtocol.ParseMachineFeatures(await module.InvokeAsync<byte[]?>("read", FeatureUuid));
            SpeedRange = FtmsProtocol.ParseSpeedRange(await module.InvokeAsync<byte[]?>("read", SpeedRangeUuid));
            InclineRange = FtmsProtocol.ParseInclinationRange(await module.InvokeAsync<byte[]?>("read", InclineRangeUuid));
            SetState(TreadmillState.Connected);
        }
        catch
        {
            await module.InvokeVoidAsync("disconnect");
            SetState(TreadmillState.Disconnected);
            throw;
        }

        try
        {
            await RequestControlAsync();
        }
        catch (TimeoutException)
        {
            // Not fatal: control is requested again when a command gets "control not permitted".
        }
        return true;
    }

    public async Task DisconnectAsync()
    {
        if (_module is null || State is TreadmillState.Disconnected)
            return;

        // Never leave the belt running without the app: stop it first (best effort).
        if (State == TreadmillState.Connected)
        {
            try
            {
                await StopAsync(StopKind.Stop);
            }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
            {
            }
        }

        _disconnectRequested = true;
        StopPolling();
        await _module.InvokeVoidAsync("disconnect");
        // Also after a lost connection: the user acknowledged it, back to "not connected".
        // The browser event that follows finds Disconnected and is ignored.
        _pending?.TrySetException(new InvalidOperationException("Treadmill disconnected."));
        SetState(TreadmillState.Disconnected);
    }

    public Task<ControlPointResult> RequestControlAsync() =>
        Kind == MachineKind.Bike ? NothingToSend() : SendAsync(FtmsProtocol.RequestControl());

    public Task<ControlPointResult> StartAsync() =>
        Kind == MachineKind.Bike ? NothingToSend() : SendAsync(FtmsProtocol.StartOrResume());

    public Task<ControlPointResult> StopAsync(StopKind kind = StopKind.Stop) => Kind switch
    {
        MachineKind.Bike when kind == StopKind.Stop => SendAsync(DomyosProtocol.SetResistance(DomyosProtocol.MinLevel)),
        MachineKind.Bike => NothingToSend(),
        _ => SendAsync(FtmsProtocol.StopOrPause(kind)),
    };

    public Task<ControlPointResult> SetSpeedAsync(double kmh) =>
        Kind == MachineKind.Bike
            ? Task.FromResult(ControlPointResult.NotSupported)
            : SendAsync(FtmsProtocol.SetTargetSpeed(ClampSpeed(kmh)));

    public Task<ControlPointResult> SetInclineAsync(double percent) =>
        SendAsync(Kind == MachineKind.Bike
            ? DomyosProtocol.SetResistance(DomyosProtocol.LevelFor(ClampIncline(percent)))
            : FtmsProtocol.SetTargetInclination(ClampIncline(percent)));

    public double ClampSpeed(double kmh)
    {
        if (Limits.MaxSpeed is { } max)
            kmh = Math.Min(kmh, max);
        return SpeedRange?.Clamp(kmh) ?? Math.Round(Math.Max(0, kmh), 2);
    }

    public double ClampIncline(double percent)
    {
        if (Limits.MaxIncline is { } max)
            percent = Math.Min(percent, max);
        return InclineRange?.Clamp(percent) ?? Math.Round(percent, 1);
    }

    // ---- Callbacks from ftms.js ----

    [JSInvokable]
    public void OnTreadmillData(byte[] frame)
    {
        if (FtmsProtocol.ParseTreadmillData(frame) is { } update)
            Publish(update);
    }

    [JSInvokable]
    public void OnDomyosData(byte[] chunk)
    {
        if (_domyosFrames.Push(chunk) is not { } frame || !DomyosProtocol.IsValid(frame))
            return;

        if (DomyosProtocol.ParseData(frame) is { } update)
            Publish(update);
        // One command in flight: the next frame from the console is its answer.
        _pending?.TrySetResult(ControlPointResult.Success);
    }

    [JSInvokable]
    public void OnControlPoint(byte[] frame)
    {
        if (FtmsProtocol.ParseControlPointResponse(frame) is { } response && response.RequestOpCode == _pendingOpCode)
            _pending?.TrySetResult(response.Result);
    }

    [JSInvokable]
    public void OnMachineStatus(byte[] frame)
    {
        if (FtmsProtocol.ParseMachineStatus(frame) is { } status)
            StatusReceived?.Invoke(status);
    }

    [JSInvokable]
    public void OnDisconnected()
    {
        // Connecting: a failed attempt is handled (and retried) by ftms.js, not a lost connection.
        if (State is TreadmillState.Disconnected or TreadmillState.ConnectionLost or TreadmillState.Connecting)
            return;

        StopPolling();
        _pending?.TrySetException(new InvalidOperationException("Treadmill disconnected."));
        SetState(_disconnectRequested ? TreadmillState.Disconnected : TreadmillState.ConnectionLost);
    }

    // ---- Bike ----

    /// <summary>Init sequence, then the measurements are polled for the whole connection.</summary>
    private async Task StartBikeAsync()
    {
        foreach (var command in DomyosProtocol.Init)
        {
            try
            {
                await SendAsync(command);
            }
            catch (TimeoutException)
            {
                // Consoles may not answer every init command: keep going, the measurements tell if it works.
            }
        }

        if (PollInterval > TimeSpan.Zero)
        {
            _polling = new CancellationTokenSource();
            _ = PollAsync(_polling.Token);
        }
    }

    private async Task PollAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token) && State == TreadmillState.Connected)
            {
                try
                {
                    await SendAsync(DomyosProtocol.RequestData());
                }
                catch (TimeoutException)
                {
                    // Lost answer: the next poll asks again.
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            // Disconnected.
        }
    }

    private void StopPolling()
    {
        _polling?.Cancel();
        _polling?.Dispose();
        _polling = null;
    }

    /// <summary>A bike command with nothing to send, with the same contract as a sent one.</summary>
    private Task<ControlPointResult> NothingToSend() =>
        State == TreadmillState.Connected
            ? Task.FromResult(ControlPointResult.Success)
            : Task.FromException<ControlPointResult>(new InvalidOperationException("Treadmill not connected."));

    // ---- Command queue ----

    private async Task<ControlPointResult> SendAsync(byte[] command)
    {
        await _commandLock.WaitAsync();
        try
        {
            var result = await WriteAndWaitAsync(command);

            // Control lost (machine reset, other app...): take it back and replay once.
            if (result == ControlPointResult.ControlNotPermitted
                && command[0] != (byte)ControlPointOpCode.RequestControl
                && await WriteAndWaitAsync(FtmsProtocol.RequestControl()) == ControlPointResult.Success)
            {
                result = await WriteAndWaitAsync(command);
            }

            return result;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private async Task<ControlPointResult> WriteAndWaitAsync(byte[] command)
    {
        if (State != TreadmillState.Connected || _module is null)
            throw new InvalidOperationException("Treadmill not connected.");

        var pending = new TaskCompletionSource<ControlPointResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingOpCode = (ControlPointOpCode)command[0];
        _pending = pending;
        try
        {
            await _module.InvokeVoidAsync("write", command);
            return await pending.Task.WaitAsync(ResponseTimeout);
        }
        finally
        {
            _pending = null;
        }
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/ftms.js");

    private void Publish(TreadmillData update)
    {
        var data = LastData?.UpdatedWith(update) ?? update;
        LastData = data;
        DataReceived?.Invoke(data);
    }

    private void SetState(TreadmillState state)
    {
        State = state;
        StateChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        StopPolling();
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("disconnect");
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
        _selfRef?.Dispose();
        _commandLock.Dispose();
    }
}

/// <summary>The machine ftms.js connected to, and the protocol it speaks.</summary>
internal sealed record ConnectedDevice(string Name, string Protocol)
{
    public const string Ftms = "ftms";
    public const string Domyos = "domyos";
}
