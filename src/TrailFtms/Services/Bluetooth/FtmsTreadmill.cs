using Microsoft.JSInterop;

namespace TrailFtms.Services.Bluetooth;

/// <summary>FTMS treadmill over Web Bluetooth (wwwroot/js/ftms.js).</summary>
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

    /// <summary>How long to wait for the Control Point answer before giving up.</summary>
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TreadmillState State { get; private set; }
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
        try
        {
            var name = await module.InvokeAsync<string?>("connect", _selfRef);
            if (name is null)
            {
                SetState(TreadmillState.Disconnected);
                return false;
            }

            DeviceName = name;
            Features = FtmsProtocol.ParseMachineFeatures(await module.InvokeAsync<byte[]?>("read", FeatureUuid));
            SpeedRange = FtmsProtocol.ParseSpeedRange(await module.InvokeAsync<byte[]?>("read", SpeedRangeUuid));
            InclineRange = FtmsProtocol.ParseInclinationRange(await module.InvokeAsync<byte[]?>("read", InclineRangeUuid));
            LastData = null;
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
        await _module.InvokeVoidAsync("disconnect");
        // Also after a lost connection: the user acknowledged it, back to "not connected".
        // The browser event that follows finds Disconnected and is ignored.
        _pending?.TrySetException(new InvalidOperationException("Treadmill disconnected."));
        SetState(TreadmillState.Disconnected);
    }

    public Task<ControlPointResult> RequestControlAsync() => SendAsync(FtmsProtocol.RequestControl());

    public Task<ControlPointResult> StartAsync() => SendAsync(FtmsProtocol.StartOrResume());

    public Task<ControlPointResult> StopAsync(StopKind kind = StopKind.Stop) => SendAsync(FtmsProtocol.StopOrPause(kind));

    public Task<ControlPointResult> SetSpeedAsync(double kmh) =>
        SendAsync(FtmsProtocol.SetTargetSpeed(ClampSpeed(kmh)));

    public Task<ControlPointResult> SetInclineAsync(double percent) =>
        SendAsync(FtmsProtocol.SetTargetInclination(ClampIncline(percent)));

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
        if (FtmsProtocol.ParseTreadmillData(frame) is not { } data)
            return;

        LastData = data;
        DataReceived?.Invoke(data);
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

        _pending?.TrySetException(new InvalidOperationException("Treadmill disconnected."));
        SetState(_disconnectRequested ? TreadmillState.Disconnected : TreadmillState.ConnectionLost);
    }

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

    private void SetState(TreadmillState state)
    {
        State = state;
        StateChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
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
