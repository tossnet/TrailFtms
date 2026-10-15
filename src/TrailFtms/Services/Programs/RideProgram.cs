namespace TrailFtms.Services.Programs;

/// <summary>Target applied from <see cref="T"/> (video time, seconds) until the next segment.</summary>
public readonly record struct ProgramSegment(double T, double Speed, double Incline);

/// <summary>A YouTube video and the treadmill targets that go with it.</summary>
public sealed record RideProgram
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string YoutubeId { get; init; } = "";
    public string? Author { get; init; }

    /// <summary>Video duration, 0 if unknown.</summary>
    public int DurationSeconds { get; init; }

    /// <summary>Sorted by <see cref="ProgramSegment.T"/>, the first one starts at 0.</summary>
    public IReadOnlyList<ProgramSegment> Segments { get; init; } = [];

    /// <summary>
    /// Drops every segment that repeats the speed and incline of the one before it: it would change nothing.
    /// <paramref name="segments"/> must be sorted.
    /// </summary>
    public static List<ProgramSegment> WithoutRepeats(IEnumerable<ProgramSegment> segments)
    {
        var result = new List<ProgramSegment>();
        foreach (var segment in segments)
        {
            if (result.Count > 0 && result[^1].Speed == segment.Speed && result[^1].Incline == segment.Incline)
                continue;
            result.Add(segment);
        }
        return result;
    }

    /// <summary>Index of the segment active at <paramref name="seconds"/> (binary search), -1 if none.</summary>
    public int SegmentIndexAt(double seconds)
    {
        int low = 0, high = Segments.Count - 1, found = -1;
        while (low <= high)
        {
            var mid = (low + high) >>> 1;
            if (Segments[mid].T <= seconds)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return found;
    }
}
