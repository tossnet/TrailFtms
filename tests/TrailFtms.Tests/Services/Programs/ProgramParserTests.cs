using System.Text.Json;
using TrailFtms.Services.Programs;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Services.Programs;

[TestClass]
public sealed class ProgramParserTests
{
    private const string Valid = """
        {
          "id": "mont-blanc-01",
          "title": "Tour du Mont Blanc, étape 1",
          "youtubeId": "KiEN4HxM4ic",
          "author": "tossnet",
          "durationSeconds": 3600,
          "segments": [
            { "t": 0,   "speed": 4.0, "incline": 0 },
            { "t": 120, "speed": 4.5, "incline": 3 },
            { "t": 600, "speed": 4.0, "incline": 8 }
          ]
        }
        """;

    private static ProgramParseResult ParseWith(Action<Dictionary<string, object?>> change)
    {
        var doc = JsonSerializer.Deserialize<Dictionary<string, object?>>(Valid)!;
        change(doc);
        return ProgramParser.Parse(JsonSerializer.Serialize(doc));
    }

    private static object[] Segments(params (double T, double Speed, double Incline)[] segments) =>
        segments.Select(s => (object)new { t = s.T, speed = s.Speed, incline = s.Incline }).ToArray();

    private static void AssertSingleError(ProgramParseResult result, ProgramErrorKind kind, double? time = null)
    {
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(new ProgramError(kind, time), result.Errors.Single());
    }

    [TestMethod]
    public void Valid_program_is_parsed()
    {
        var result = ProgramParser.Parse(Valid);

        Assert.IsTrue(result.IsValid);
        Assert.IsEmpty(result.Errors);
        var program = result.Program!;
        Assert.AreEqual("mont-blanc-01", program.Id);
        Assert.AreEqual("Tour du Mont Blanc, étape 1", program.Title);
        Assert.AreEqual("KiEN4HxM4ic", program.YoutubeId);
        Assert.AreEqual("tossnet", program.Author);
        Assert.AreEqual(3600, program.DurationSeconds);
        Assert.AreEqual(new ProgramSegment(120, 4.5, 3), program.Segments[1]);
    }

    [TestMethod]
    public void Every_shipped_program_is_valid()
    {
        var programsDir = Path.Combine(WwwrootHttpHandler.WwwrootPath, "data", "programs");
        var files = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(programsDir, "index.json")))!;

        Assert.IsNotEmpty(files);
        foreach (var file in files)
        {
            var result = ProgramParser.Parse(File.ReadAllText(Path.Combine(programsDir, file)));
            Assert.IsTrue(result.IsValid, $"{file}: {string.Join(", ", result.Errors)}");
            Assert.AreEqual(Path.GetFileNameWithoutExtension(file), result.Program!.Id, $"{file}: id must match the file name");
        }
    }

    [TestMethod]
    public void Segments_are_sorted_and_youtube_link_is_accepted()
    {
        var result = ParseWith(d =>
        {
            d["youtubeId"] = "https://youtu.be/KiEN4HxM4ic";
            d["segments"] = Segments((600, 4, 8), (0, 4, 0), (120, 4.5, 3));
        });

        Assert.IsTrue(result.IsValid);
        CollectionAssert.AreEqual(new double[] { 0, 120, 600 }, result.Program!.Segments.Select(s => s.T).ToArray());
        Assert.AreEqual("KiEN4HxM4ic", result.Program.YoutubeId);
    }

    [TestMethod]
    public void Comments_trailing_commas_and_property_case_are_tolerated()
    {
        var json = """
            {
              // shipped by a contributor
              "Id": "test", "TITLE": "Test", "youtubeId": "KiEN4HxM4ic",
              "segments": [ { "t": 0, "speed": 3, "incline": 1 }, ],
            }
            """;

        Assert.IsTrue(ProgramParser.Parse(json).IsValid);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not json")]
    [DataRow("[1, 2]")]
    [DataRow("null")]
    [DataRow("""{ "segments": "nope" }""")]
    public void Invalid_json_is_reported(string json)
    {
        AssertSingleError(ProgramParser.Parse(json), ProgramErrorKind.InvalidJson);
    }

    [TestMethod]
    public void Missing_and_invalid_fields_are_all_reported()
    {
        var result = ProgramParser.Parse("""{ "durationSeconds": -1, "segments": [] }""");

        CollectionAssert.AreEquivalent(
            new[]
            {
                new ProgramError(ProgramErrorKind.MissingId),
                new ProgramError(ProgramErrorKind.MissingTitle),
                new ProgramError(ProgramErrorKind.InvalidYoutubeId),
                new ProgramError(ProgramErrorKind.InvalidDuration),
                new ProgramError(ProgramErrorKind.NoSegments),
            },
            result.Errors.ToArray());
    }

    [TestMethod]
    [DataRow("Mont Blanc")]
    [DataRow("mont_blanc")]
    [DataRow("-mont")]
    [DataRow("Mont-Blanc")]
    public void Id_must_be_a_slug(string id)
    {
        AssertSingleError(ParseWith(d => d["id"] = id), ProgramErrorKind.InvalidId);
    }

    [TestMethod]
    public void First_segment_must_start_at_zero()
    {
        AssertSingleError(
            ParseWith(d => d["segments"] = Segments((10, 4, 0), (120, 4, 2))),
            ProgramErrorKind.FirstSegmentNotAtStart, 10);
    }

    [TestMethod]
    public void Duplicate_times_are_rejected()
    {
        AssertSingleError(
            ParseWith(d => d["segments"] = Segments((0, 4, 0), (120, 4, 2), (120, 5, 3))),
            ProgramErrorKind.DuplicateTime, 120);
    }

    [TestMethod]
    public void Segment_after_end_is_rejected_only_when_duration_is_known()
    {
        AssertSingleError(
            ParseWith(d => d["segments"] = Segments((0, 4, 0), (3600, 4, 2))),
            ProgramErrorKind.SegmentAfterEnd, 3600);

        Assert.IsTrue(ParseWith(d =>
        {
            d["durationSeconds"] = 0;
            d["segments"] = Segments((0, 4, 0), (9999, 4, 2));
        }).IsValid);
    }

    [TestMethod]
    [DataRow(-0.5, 0.0, ProgramErrorKind.SpeedOutOfRange)]
    [DataRow(31.0, 0.0, ProgramErrorKind.SpeedOutOfRange)]
    [DataRow(4.0, -11.0, ProgramErrorKind.InclineOutOfRange)]
    [DataRow(4.0, 31.0, ProgramErrorKind.InclineOutOfRange)]
    public void Out_of_range_targets_are_rejected(double speed, double incline, ProgramErrorKind kind)
    {
        AssertSingleError(
            ParseWith(d => d["segments"] = Segments((0, 4, 0), (60, speed, incline))),
            kind, 60);
    }

    [TestMethod]
    public void Serialize_is_readable_with_one_segment_per_line()
    {
        var json = ProgramParser.Serialize(ProgramParser.Parse(Valid).Program!);

        StringAssert.Contains(json, "\"title\": \"Tour du Mont Blanc, étape 1\"");
        StringAssert.Contains(json, "    { \"t\": 120, \"speed\": 4.5, \"incline\": 3 },\n");
        StringAssert.Contains(json, "    { \"t\": 600, \"speed\": 4, \"incline\": 8 }\n  ]");
    }

    [TestMethod]
    public void Serialize_round_trips()
    {
        var program = ProgramParser.Parse(Valid).Program!;

        var copy = ProgramParser.Parse(ProgramParser.Serialize(program)).Program!;

        Assert.AreEqual(program with { Segments = [] }, copy with { Segments = [] });
        CollectionAssert.AreEqual(program.Segments.ToArray(), copy.Segments.ToArray());
    }
}
