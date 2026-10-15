using TrailFtms.Services.Programs;

namespace TrailFtms.Services.Ride;

/// <summary>
/// Records a free ride ("try a video"): the speed and incline the user runs at, indexed on video time,
/// to make a program from it. A value counts once kept for <see cref="AdjustmentWatch.StableSeconds"/>
/// (belt ramps and incline travel are skipped) and is dated from when it started to change.
/// </summary>
public sealed class RideRecorder(double speedStep, double inclineStep)
{
    /// <summary>A duration below this share of the longest one seen is an ad, not the video.</summary>
    private const double AdDurationRatio = 0.5;

    /// <summary>Going back in the video by more than this is a seek, not jitter.</summary>
    private const double SeekBackSeconds = 1;

    private AdjustmentWatch _speedWatch = new();
    private AdjustmentWatch _inclineWatch = new();
    private readonly List<RideAdjustment> _changes = [];
    private double? _speed;
    private double? _incline;
    private ProgramSegment? _start;
    private double _lastTime;
    private double _videoDuration;
    private IReadOnlyList<ProgramSegment>? _segments;

    /// <summary>True once the first speed and incline are known: there is something to save.</summary>
    public bool HasData => _start is not null;

    /// <summary>Incremented on every recorded change.</summary>
    public int Revision { get; private set; }

    /// <summary>The ride so far, sorted, the first segment at 0.</summary>
    public IReadOnlyList<ProgramSegment> Segments => _segments ??= _start is { } start
        ? RideAdjustment.ApplyAll(new RideProgram { Segments = [start] }, _changes).Segments
        : [];

    /// <summary>
    /// Feeds a measure taken while the video plays, at video time <paramref name="time"/>.
    /// Ads are skipped. A seek back drops what was recorded after the new position: that part is ridden again.
    /// </summary>
    public void Observe(double time, double duration, double speed, double incline)
    {
        if (duration > 0)
        {
            _videoDuration = Math.Max(_videoDuration, duration);
            if (duration < _videoDuration * AdDurationRatio)
                return;
        }

        if (time < _lastTime - SeekBackSeconds)
        {
            if (_changes.RemoveAll(c => c.T >= time) > 0)
                Changed();
            // The belt may not match the program at the new position: its value is recorded again once stable.
            _speedWatch = new();
            _inclineWatch = new();
        }
        _lastTime = time;

        if (_speedWatch.Observe(time, speed, speedStep) is { } speedChange)
            Record(AdjustedValue.Speed, speedChange.T, speedChange.Value);
        if (_inclineWatch.Observe(time, incline, inclineStep) is { } inclineChange)
            Record(AdjustedValue.Incline, inclineChange.T, inclineChange.Value);
    }

    /// <summary>The recorded ride as a program. The duration is the video's when known.</summary>
    public RideProgram ToProgram(string id, string title, string youtubeId)
    {
        var segments = Segments;
        var duration = (int)Math.Floor(_videoDuration);
        return new RideProgram
        {
            Id = id,
            Title = title,
            YoutubeId = youtubeId,
            DurationSeconds = segments.Count > 0 && duration > segments[^1].T ? duration : 0,
            Segments = segments,
        };
    }

    private void Record(AdjustedValue kind, double t, double value)
    {
        if (kind == AdjustedValue.Speed)
            _speed = value;
        else
            _incline = value;

        if (_start is null)
        {
            // The ride starts with the first known speed and incline, whenever the user settled on them.
            if (_speed is { } speed && _incline is { } incline)
            {
                _start = new ProgramSegment(0, Math.Round(speed, 1), Math.Round(incline, 1));
                Changed();
            }
            return;
        }
        _changes.Add(new(t, kind, Math.Round(value, 1)));
        Changed();
    }

    private void Changed()
    {
        _segments = null;
        Revision++;
    }
}
