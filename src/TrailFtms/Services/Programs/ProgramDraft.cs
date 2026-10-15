using System.Globalization;
using System.Text;

namespace TrailFtms.Services.Programs;

/// <summary>Mutable program being edited. Segments are kept sorted, one per whole second.</summary>
public sealed class ProgramDraft
{
    private readonly List<ProgramSegment> _segments = [];

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string YoutubeId { get; set; } = "";
    public string? Author { get; set; }
    public int DurationSeconds { get; set; }

    public IReadOnlyList<ProgramSegment> Segments => _segments;

    public static ProgramDraft From(RideProgram program)
    {
        var draft = new ProgramDraft
        {
            Id = program.Id,
            Title = program.Title,
            YoutubeId = program.YoutubeId,
            Author = program.Author,
            DurationSeconds = program.DurationSeconds,
        };
        draft._segments.AddRange(program.Segments.OrderBy(s => s.T));
        return draft;
    }

    public RideProgram ToProgram() => new()
    {
        Id = Id.Trim(),
        Title = Title.Trim(),
        YoutubeId = YoutubeId.Trim(),
        Author = string.IsNullOrWhiteSpace(Author) ? null : Author.Trim(),
        DurationSeconds = DurationSeconds,
        Segments = RideProgram.WithoutRepeats(_segments),
    };

    /// <summary>Segment active at <paramref name="seconds"/>, null before the first one.</summary>
    public ProgramSegment? ActiveAt(double seconds)
    {
        ProgramSegment? active = null;
        foreach (var segment in _segments)
        {
            if (segment.T > seconds)
                break;
            active = segment;
        }
        return active;
    }

    /// <summary>
    /// Adds a point at <paramref name="seconds"/> (rounded down to the second), or replaces the one
    /// already there. Points that would repeat the previous one are dropped (adding the same point twice
    /// keeps a single one). Returns the index of the point in force at that second.
    /// </summary>
    public int SetPoint(double seconds, double speed, double incline)
    {
        var t = Math.Max(0, Math.Floor(seconds));
        var segment = new ProgramSegment(t, Math.Round(speed, 1), Math.Round(incline, 1));
        var index = _segments.FindIndex(s => s.T >= t);
        if (index < 0)
            _segments.Add(segment);
        else if (_segments[index].T == t)
            _segments[index] = segment;
        else
            _segments.Insert(index, segment);

        var kept = RideProgram.WithoutRepeats(_segments);
        if (kept.Count != _segments.Count)
        {
            _segments.Clear();
            _segments.AddRange(kept);
        }
        return _segments.FindLastIndex(s => s.T <= t);
    }

    public void Update(int index, double speed, double incline) =>
        _segments[index] = _segments[index] with { Speed = Math.Round(speed, 1), Incline = Math.Round(incline, 1) };

    public void Remove(int index) => _segments.RemoveAt(index);

    /// <summary>"Tour du Mont Blanc, étape 1" gives "tour-du-mont-blanc-etape-1".</summary>
    public static string Slugify(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingDash = false;
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue; // accents
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingDash && builder.Length > 0)
                    builder.Append('-');
                builder.Append(char.ToLowerInvariant(c));
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }
        return builder.ToString();
    }
}
