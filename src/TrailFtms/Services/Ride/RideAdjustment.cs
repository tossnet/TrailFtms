using TrailFtms.Services.Programs;

namespace TrailFtms.Services.Ride;

public enum AdjustedValue
{
    Speed,
    Incline,
}

/// <summary>A change made on the treadmill console during a ride, from video time <see cref="T"/>, in program units.</summary>
public readonly record struct RideAdjustment(double T, AdjustedValue Kind, double Value)
{
    /// <summary>
    /// The program the user actually rode: each adjustment becomes a point, and its value is carried over
    /// the following points until the program changes that value (the engine only sends changes, so the
    /// treadmill kept the user's value until then). Later adjustments win over earlier ones.
    /// </summary>
    public static RideProgram ApplyAll(RideProgram program, IEnumerable<RideAdjustment> adjustments)
    {
        var draft = ProgramDraft.From(program);
        foreach (var adjustment in adjustments)
        {
            var t = Math.Floor(adjustment.T);
            var active = draft.ActiveAt(t) ?? draft.Segments[0];
            var original = adjustment.Get(active);
            var index = draft.SetPoint(t, adjustment.SpeedFor(active), adjustment.InclineFor(active));
            for (var i = index + 1; i < draft.Segments.Count && adjustment.Get(draft.Segments[i]) == original; i++)
                draft.Update(i, adjustment.SpeedFor(draft.Segments[i]), adjustment.InclineFor(draft.Segments[i]));
        }
        return draft.ToProgram();
    }

    private double Get(ProgramSegment segment) => Kind == AdjustedValue.Speed ? segment.Speed : segment.Incline;

    private double SpeedFor(ProgramSegment segment) => Kind == AdjustedValue.Speed ? Value : segment.Speed;

    private double InclineFor(ProgramSegment segment) => Kind == AdjustedValue.Incline ? Value : segment.Incline;
}

/// <summary>
/// Watches one measured value (speed or incline) against the last one sent. Once the treadmill has reached
/// the sent value, a different value kept for <see cref="StableSeconds"/> of video is a change made on the
/// console. Values still moving towards a new target (belt ramp, incline motor) are never reported.
/// </summary>
internal sealed class AdjustmentWatch
{
    internal const double StableSeconds = 3;

    private double? _expected;
    private bool _reached;
    private double? _deviationSince;
    private double? _candidate;
    private double _candidateSince;

    /// <summary>A value was sent: wait for the treadmill to reach it.</summary>
    public void Expect(double value)
    {
        _expected = value;
        Rearm();
    }

    /// <summary>Wait again for the treadmill to reach the expected value (after a pause, the belt may have changed).</summary>
    public void Rearm()
    {
        _reached = false;
        _deviationSince = _candidate = null;
    }

    /// <summary>
    /// Feeds a measure at video time <paramref name="time"/>, snapped to <paramref name="step"/>.
    /// Returns the change, dated from its first step, once the user kept it long enough.
    /// </summary>
    public (double T, double Value)? Observe(double time, double measured, double step)
    {
        if (_expected is not { } expected)
            return null;

        var value = Math.Round(Math.Round(measured / step) * step, 2);
        if (Same(value, expected, step))
        {
            _reached = true;
            _deviationSince = _candidate = null;
            return null;
        }
        if (!_reached)
            return null;

        // A seek back would date the change before it happened: start over.
        if (time < _deviationSince || time < _candidateSince)
            _deviationSince = _candidate = null;

        _deviationSince ??= time;
        if (_candidate is not { } candidate || !Same(value, candidate, step))
        {
            _candidate = value;
            _candidateSince = time;
            return null;
        }
        if (time - _candidateSince < StableSeconds)
            return null;

        var change = (_deviationSince.Value, candidate);
        _expected = candidate; // the user's value is the new reference
        _deviationSince = _candidate = null;
        return change;
    }

    private static bool Same(double a, double b, double step) => Math.Abs(a - b) < step / 2;
}
