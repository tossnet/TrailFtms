using TrailFtms.Services.Bluetooth;
using TrailFtms.Services.Programs;
using TrailFtms.Services.Video;

namespace TrailFtms.Services.Ride;

public enum RideStatus
{
    Idle,
    /// <summary>Following the video.</summary>
    Running,
    /// <summary>Video ended: bringing incline to 0, then speed to minimum, then stop.</summary>
    CoolingDown,
    Finished,
    /// <summary>Stopped for <see cref="RideEngine.HaltReason"/>. Nothing is sent until a new start.</summary>
    Halted,
}

public enum HaltReason
{
    None,
    StoppedByUser,
    StoppedFromTreadmill,
    ConnectionLost,
}

public enum RideWarning
{
    CommandRejected,
    NoResponse,
}

/// <summary>
/// The video drives the treadmill: every tick reads the video time, finds the program segment
/// and sends speed / incline only when they change. Always indexed on video time, never on a clock,
/// so ads, buffering and seeks keep the treadmill in sync.
/// </summary>
public sealed class RideEngine : IAsyncDisposable
{
    internal static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);
    internal const int CooldownStepTicks = 8; // one cool-down step every 2 s
    private const double CooldownSpeedStep = 0.5;
    /// <summary>A duration below this share of the ride video is an ad, not the video.</summary>
    private const double AdDurationRatio = 0.5;

    private readonly IFtmsTreadmill _treadmill;
    private readonly IVideoPlayer _video;
    private readonly TimeProvider _time;
    private CancellationTokenSource? _loop;
    private bool _ticking;
    private bool _beltStarted;
    private bool _pauseApplied;
    private bool _selfStopped;
    private double? _sentSpeed;
    private double? _sentIncline;
    private int _cooldownTicks;
    private double _videoDuration;
    private readonly AdjustmentWatch _speedWatch = new();
    private readonly AdjustmentWatch _inclineWatch = new();
    private readonly List<RideAdjustment> _adjustments = [];

    public RideEngine(IFtmsTreadmill treadmill, IVideoPlayer video, TimeProvider? time = null)
    {
        _treadmill = treadmill;
        _video = video;
        _time = time ?? TimeProvider.System;
        _video.StateChanged += OnVideoStateChanged;
        _treadmill.StateChanged += OnTreadmillStateChanged;
        _treadmill.StatusReceived += OnMachineStatus;
    }

    public RideSettings Settings { get; set; } = new();
    public RideProgram? Program { get; private set; }
    public RideStatus Status { get; private set; }
    public HaltReason HaltReason { get; private set; }

    public double VideoTime { get; private set; }
    public int SegmentIndex { get; private set; } = -1;

    /// <summary>Speed the program asks for, after factor, user ceiling and machine range.</summary>
    public double? TargetSpeed { get; private set; }

    /// <summary>Incline the program asks for, after user ceiling and machine range.</summary>
    public double? TargetIncline { get; private set; }

    /// <summary>Seconds until the next segment, null on the last one.</summary>
    public double? NextChangeIn { get; private set; }

    public bool IsActive => Status is RideStatus.Running or RideStatus.CoolingDown;

    /// <summary>A YouTube ad is playing: the treadmill keeps its current pace.</summary>
    public bool IsAdPlaying { get; private set; }

    /// <summary>Changes made on the treadmill console while following the video, in ride order.</summary>
    public IReadOnlyList<RideAdjustment> Adjustments => _adjustments;

    public event Action? Changed;
    public event Action<RideWarning>? WarningRaised;

    /// <summary>Starts following the video. The belt starts when the video plays.</summary>
    public void Start(RideProgram program)
    {
        StopLoop();
        if (!ReferenceEquals(program, Program))
            _adjustments.Clear(); // resuming the same ride keeps them
        Program = program;
        Status = RideStatus.Running;
        HaltReason = HaltReason.None;
        SegmentIndex = -1;
        TargetSpeed = TargetIncline = NextChangeIn = null;
        _sentSpeed = _sentIncline = null;
        _beltStarted = _pauseApplied = _selfStopped = IsAdPlaying = false;
        _videoDuration = program.DurationSeconds;

        _loop = new CancellationTokenSource();
        _ = RunLoopAsync(_loop.Token);
        Changed?.Invoke();
    }

    /// <summary>STOP: stops the sync, the belt and the video.</summary>
    public async Task StopAsync()
    {
        Halt(HaltReason.StoppedByUser);
        _selfStopped = true;
        try
        {
            await _treadmill.StopAsync(StopKind.Stop);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
        {
            // Not connected or no answer: nothing more the app can do, the safety key remains.
        }
        await _video.PauseAsync();
    }

    internal async Task TickAsync()
    {
        if (_ticking || !IsActive || Program is null)
            return;

        _ticking = true;
        try
        {
            if (_treadmill.State != TreadmillState.Connected)
            {
                Halt(HaltReason.ConnectionLost);
                return;
            }

            if (Status == RideStatus.CoolingDown)
            {
                await CoolDownAsync();
                return;
            }

            IsAdPlaying = _video.State is VideoState.Playing or VideoState.Buffering or VideoState.Ended
                && await IsAdAsync();
            if (IsAdPlaying)
                return;

            switch (_video.State)
            {
                case VideoState.Playing:
                    await FollowVideoAsync(Program);
                    break;
                // Buffering in the middle of the ride: keep following (targets do not change while the
                // time is frozen). Before the first start or after a pause, wait until the video really plays.
                case VideoState.Buffering when _beltStarted && !_pauseApplied:
                    await FollowVideoAsync(Program);
                    break;
                case VideoState.Paused:
                    await ApplyPauseAsync();
                    break;
                case VideoState.Ended:
                    Status = RideStatus.CoolingDown;
                    _cooldownTicks = CooldownStepTicks - 1; // first step right away
                    await CoolDownAsync();
                    break;
            }
        }
        catch (InvalidOperationException)
        {
            Halt(HaltReason.ConnectionLost);
        }
        finally
        {
            _ticking = false;
            Changed?.Invoke();
        }
    }

    private async Task FollowVideoAsync(RideProgram program)
    {
        VideoTime = await _video.GetCurrentTimeAsync();
        SegmentIndex = program.SegmentIndexAt(VideoTime);
        if (SegmentIndex < 0)
            return;

        var segments = program.Segments;
        NextChangeIn = SegmentIndex + 1 < segments.Count ? segments[SegmentIndex + 1].T - VideoTime : null;
        var segment = segments[SegmentIndex];
        TargetSpeed = SpeedTarget(segment.Speed * Settings.SpeedFactor);
        TargetIncline = InclineAhead(segments);

        if (_pauseApplied)
        {
            _pauseApplied = false;
            if (Settings.PauseBehavior == PauseBehavior.PauseTreadmill)
                _beltStarted = false;
        }

        if (!_beltStarted)
        {
            _beltStarted = true;
            _selfStopped = false;
            await SendAsync(_treadmill.StartAsync());
        }
        await SetSpeedAsync(TargetSpeed.Value);
        await SetInclineAsync(TargetIncline.Value);
        WatchAdjustments();
    }

    /// <summary>
    /// Ads play inside the same player: time and duration become the ad's, which would replay the
    /// first segments (and an ad ending would look like the video ending). Ads are much shorter than
    /// a ride video, so the longest duration seen is the video and a much shorter one is an ad.
    /// </summary>
    private async Task<bool> IsAdAsync()
    {
        var duration = await _video.GetDurationAsync();
        if (duration <= 0)
            return false; // not known yet
        _videoDuration = Math.Max(_videoDuration, duration);
        return duration < _videoDuration * AdDurationRatio;
    }

    private void WatchAdjustments()
    {
        var data = _treadmill.LastData;
        if (!IsBike && data?.SpeedKmh is { } speed && _speedWatch.Observe(VideoTime, speed, SpeedStep) is { } speedChange)
        {
            var value = Settings.SpeedFactor > 0 ? speedChange.Value / Settings.SpeedFactor : speedChange.Value;
            _adjustments.Add(new(speedChange.T, AdjustedValue.Speed, Math.Round(value, 1)));
        }
        if (data?.InclinePercent is { } incline && _inclineWatch.Observe(VideoTime, incline, InclineStep) is { } inclineChange)
            _adjustments.Add(new(inclineChange.T, AdjustedValue.Incline, inclineChange.Value));
    }

    /// <summary>
    /// Incline of the current segment, or of the next incline change once the treadmill needs
    /// to start moving so that it is reached on time (the motor moves one step every InclineSecondsPerStep).
    /// A bike resistance changes at once.
    /// </summary>
    private double InclineAhead(IReadOnlyList<ProgramSegment> segments)
    {
        var current = InclineTarget(segments[SegmentIndex].Incline);
        if (Settings.InclineSecondsPerStep <= 0 || IsBike)
            return current;

        for (var i = SegmentIndex + 1; i < segments.Count; i++)
        {
            var next = InclineTarget(segments[i].Incline);
            if (next == current)
                continue; // speed-only change
            var travel = Math.Abs(next - current) / InclineStep * Settings.InclineSecondsPerStep;
            return segments[i].T - VideoTime <= travel ? next : current;
        }
        return current;
    }

    private double InclineStep => _treadmill.InclineRange?.Step is > 0 and var s ? s : 1.0;

    private double SpeedStep => _treadmill.SpeedRange?.Step is > 0 and var s ? s : 0.1;

    /// <summary>The user pedals: the program speed is only shown, never sent nor watched.</summary>
    private bool IsBike => _treadmill.Kind == MachineKind.Bike;

    private async Task ApplyPauseAsync()
    {
        if (_pauseApplied || !_beltStarted)
            return;

        _pauseApplied = true;
        // Changes made while the video is paused (slowing down to drink...) are not part of the ride.
        _speedWatch.Rearm();
        _inclineWatch.Rearm();
        switch (Settings.PauseBehavior)
        {
            case PauseBehavior.SlowDown:
                await SetSpeedAsync(Math.Min(SpeedTarget(Settings.PauseSpeed), _sentSpeed ?? double.MaxValue));
                break;
            case PauseBehavior.PauseTreadmill:
                _selfStopped = true;
                await SendAsync(_treadmill.StopAsync(StopKind.Pause));
                break;
        }
    }

    private async Task CoolDownAsync()
    {
        if (++_cooldownTicks < CooldownStepTicks)
            return;
        _cooldownTicks = 0;

        var incline = _sentIncline ?? 0;
        var lowerIncline = InclineTarget(incline > 0 ? Math.Max(0, incline - InclineStep) : Math.Min(0, incline + InclineStep));
        if (lowerIncline != incline)
        {
            await SetInclineAsync(lowerIncline);
            return;
        }

        var speed = _sentSpeed ?? 0;
        var lowerSpeed = SpeedTarget(speed - CooldownSpeedStep);
        if (lowerSpeed < speed)
        {
            await SetSpeedAsync(lowerSpeed);
            return;
        }

        _selfStopped = true;
        await SendAsync(_treadmill.StopAsync(StopKind.Stop));
        Status = RideStatus.Finished;
        StopLoop();
    }

    // Values are recorded as sent before the command: a rejected or unanswered command
    // is not retried in a loop, the next change will try again.
    private Task SetSpeedAsync(double speed)
    {
        if (IsBike || speed == _sentSpeed)
            return Task.CompletedTask;
        _sentSpeed = speed;
        _speedWatch.Expect(speed);
        return SendAsync(_treadmill.SetSpeedAsync(speed));
    }

    private Task SetInclineAsync(double incline)
    {
        if (incline == _sentIncline)
            return Task.CompletedTask;
        _sentIncline = incline;
        _inclineWatch.Expect(incline);
        return SendAsync(_treadmill.SetInclineAsync(incline));
    }

    private async Task SendAsync(Task<ControlPointResult> command)
    {
        try
        {
            if (await command != ControlPointResult.Success)
                WarningRaised?.Invoke(RideWarning.CommandRejected);
        }
        catch (TimeoutException)
        {
            WarningRaised?.Invoke(RideWarning.NoResponse);
        }
    }

    private double SpeedTarget(double kmh) => _treadmill.ClampSpeed(kmh);

    private double InclineTarget(double percent) => _treadmill.ClampIncline(percent);

    private void Halt(HaltReason reason)
    {
        if (!IsActive)
            return;
        Status = RideStatus.Halted;
        HaltReason = reason;
        StopLoop();
        Changed?.Invoke();
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TickInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
                await TickAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StopLoop()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
    }

    // Seek, pause, play: react now rather than at the next tick.
    private void OnVideoStateChanged() => _ = TickAsync();

    private void OnTreadmillStateChanged()
    {
        if (_treadmill.State == TreadmillState.Disconnected)
            Halt(HaltReason.StoppedByUser); // "Disconnect" button
        else if (_treadmill.State != TreadmillState.Connected)
            Halt(HaltReason.ConnectionLost);
    }

    private void OnMachineStatus(MachineStatus status)
    {
        // A stop from the console or the safety key wins: never restart the belt behind the user's back.
        var stoppedOnMachine = status == MachineStatus.StoppedBySafetyKey
            || (status == MachineStatus.StoppedOrPausedByUser && !_selfStopped);
        if (!stoppedOnMachine || !IsActive)
            return;

        Halt(HaltReason.StoppedFromTreadmill);
        _ = _video.PauseAsync();
    }

    public ValueTask DisposeAsync()
    {
        StopLoop();
        _video.StateChanged -= OnVideoStateChanged;
        _treadmill.StateChanged -= OnTreadmillStateChanged;
        _treadmill.StatusReceived -= OnMachineStatus;
        return ValueTask.CompletedTask;
    }
}
