using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TrailFtms.Services.Video;

namespace TrailFtms.Services.Programs;

public enum ProgramErrorKind
{
    InvalidJson,
    MissingId,
    InvalidId,
    MissingTitle,
    InvalidYoutubeId,
    InvalidDuration,
    NoSegments,
    FirstSegmentNotAtStart,
    DuplicateTime,
    SegmentAfterEnd,
    SpeedOutOfRange,
    InclineOutOfRange,
}

/// <summary>A validation error, located by the video time of the faulty segment when relevant.</summary>
public readonly record struct ProgramError(ProgramErrorKind Kind, double? Time = null);

public sealed record ProgramParseResult(RideProgram? Program, IReadOnlyList<ProgramError> Errors)
{
    public bool IsValid => Program is not null;
}

/// <summary>Reads, validates and writes program JSON files.</summary>
public static partial class ProgramParser
{
    public const double MaxSpeed = 30;
    public const double MinIncline = -10;
    public const double MaxIncline = 30;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdRegex();

    /// <summary>
    /// Parses and validates a program. Segments are sorted by time and repeated ones dropped, the YouTube id may be a link.
    /// Returns every error found, the program is null when there is at least one.
    /// </summary>
    public static ProgramParseResult Parse(string json)
    {
        RideProgram? raw;
        try
        {
            raw = JsonSerializer.Deserialize(json, ProgramJsonContext.Default.RideProgram);
        }
        catch (JsonException)
        {
            raw = null;
        }
        if (raw is null)
            return new(null, [new(ProgramErrorKind.InvalidJson)]);

        var program = raw with
        {
            Id = raw.Id?.Trim() ?? "",
            Title = raw.Title?.Trim() ?? "",
            YoutubeId = YouTubeUrl.ParseVideoId(raw.YoutubeId) ?? "",
            Author = string.IsNullOrWhiteSpace(raw.Author) ? null : raw.Author.Trim(),
            Segments = (raw.Segments ?? []).OrderBy(s => s.T).ToArray(),
        };

        var errors = Validate(program);
        if (errors.Count > 0)
            return new(null, errors);
        return new(program with { Segments = RideProgram.WithoutRepeats(program.Segments) }, errors);
    }

    // Exported files are meant to be read and edited by hand: keep accents as is, same line endings everywhere.
    private static readonly ProgramJsonContext Writer = new(
        new JsonSerializerOptions(ProgramJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NewLine = "\n",
        });

    /// <summary>Writes a program as indented JSON, one segment per line.</summary>
    public static string Serialize(RideProgram program)
    {
        var json = JsonSerializer.Serialize(program with { Segments = [] }, Writer.RideProgram);
        if (program.Segments.Count == 0)
            return json;

        var segments = program.Segments.Select(s =>
            $"    {{ \"t\": {Number(s.T)}, \"speed\": {Number(s.Speed)}, \"incline\": {Number(s.Incline)} }}");
        return json.Replace("\"segments\": []", $"\"segments\": [\n{string.Join(",\n", segments)}\n  ]");
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static List<ProgramError> Validate(RideProgram program)
    {
        var errors = new List<ProgramError>();

        if (program.Id.Length == 0)
            errors.Add(new(ProgramErrorKind.MissingId));
        else if (!IdRegex().IsMatch(program.Id))
            errors.Add(new(ProgramErrorKind.InvalidId));
        if (program.Title.Length == 0)
            errors.Add(new(ProgramErrorKind.MissingTitle));
        if (program.YoutubeId.Length == 0)
            errors.Add(new(ProgramErrorKind.InvalidYoutubeId));
        if (program.DurationSeconds < 0)
            errors.Add(new(ProgramErrorKind.InvalidDuration));

        var segments = program.Segments;
        if (segments.Count == 0)
        {
            errors.Add(new(ProgramErrorKind.NoSegments));
            return errors;
        }
        if (segments[0].T != 0)
            errors.Add(new(ProgramErrorKind.FirstSegmentNotAtStart, segments[0].T));

        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            if (i > 0 && s.T == segments[i - 1].T)
                errors.Add(new(ProgramErrorKind.DuplicateTime, s.T));
            if (program.DurationSeconds > 0 && s.T >= program.DurationSeconds)
                errors.Add(new(ProgramErrorKind.SegmentAfterEnd, s.T));
            if (s.Speed is < 0 or > MaxSpeed || double.IsNaN(s.Speed))
                errors.Add(new(ProgramErrorKind.SpeedOutOfRange, s.T));
            if (s.Incline is < MinIncline or > MaxIncline || double.IsNaN(s.Incline))
                errors.Add(new(ProgramErrorKind.InclineOutOfRange, s.T));
        }
        return errors;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RideProgram))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class ProgramJsonContext : JsonSerializerContext;
