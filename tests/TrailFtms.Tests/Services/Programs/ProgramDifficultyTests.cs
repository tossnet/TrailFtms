using TrailFtms.Services.Programs;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Services.Programs;

[TestClass]
public sealed class ProgramDifficultyTests
{
    private static RideProgram Make(int duration, ProgramSegment[] segments) =>
        new() { Id = "p", Title = "P", YoutubeId = "KiEN4HxM4ic", DurationSeconds = duration, Segments = segments };

    [TestMethod]
    public void Distance_and_elevation_gain_come_from_each_segment_duration()
    {
        // 10 min at 6 km/h flat, then 10 min at 6 km/h and 10 % until the end of the video.
        var difficulty = ProgramDifficulty.Of(Make(1200, [new(0, 6, 0), new(600, 6, 10)]));

        Assert.AreEqual(2.0, difficulty.DistanceKm, 1e-9);
        Assert.AreEqual(100, difficulty.ElevationGainM, 1e-9);
        Assert.AreEqual(10, difficulty.MaxIncline);
    }

    [TestMethod]
    public void Downhill_adds_no_gain_and_counts_as_flat()
    {
        var difficulty = ProgramDifficulty.Of(Make(600, [new(0, 5, -3)]));

        Assert.AreEqual(0, difficulty.ElevationGainM);
        Assert.AreEqual(ProgramDifficulty.Met(5, 0), difficulty.AverageMet, 1e-9);
    }

    [TestMethod]
    public void Met_uses_walking_then_running_equation()
    {
        // ACSM: walking 6 km/h (100 m/min) flat = 3.5 + 10 = 13.5 ml/kg/min.
        Assert.AreEqual(13.5 / 3.5, ProgramDifficulty.Met(6, 0), 1e-9);
        // Walking 6 km/h at 10 %: + 1.8 x 100 x 0.1 = 18.
        Assert.AreEqual(31.5 / 3.5, ProgramDifficulty.Met(6, 10), 1e-9);
        // Running 12 km/h (200 m/min) at 5 %: 3.5 + 40 + 0.9 x 200 x 0.05 = 52.5.
        Assert.AreEqual(52.5 / 3.5, ProgramDifficulty.Met(12, 5), 1e-9);
    }

    [TestMethod]
    public void Short_intense_program_is_rated_by_intensity()
    {
        var difficulty = ProgramDifficulty.Of(Make(600, [new(0, 12, 0)])); // 10 min run, 11.4 MET

        Assert.IsLessThan(150, difficulty.MetMinutes);
        Assert.AreEqual(5, difficulty.Level);
    }

    [TestMethod]
    public void Long_easy_program_is_rated_by_volume()
    {
        var difficulty = ProgramDifficulty.Of(Make(7200, [new(0, 4, 0)])); // 2 h walk, 2.9 MET

        Assert.IsLessThan(4, difficulty.AverageMet);
        Assert.AreEqual(3, difficulty.Level);
    }

    [TestMethod]
    public void Empty_program_is_easy()
    {
        var difficulty = ProgramDifficulty.Of(Make(0, []));

        Assert.AreEqual(new ProgramDifficulty(1, 0, 0, 0, 0, 0), difficulty);
    }

    [TestMethod]
    public void Unknown_duration_ignores_the_last_segment()
    {
        var difficulty = ProgramDifficulty.Of(Make(0, [new(0, 6, 0), new(600, 18, 15)]));

        Assert.AreEqual(1.0, difficulty.DistanceKm, 1e-9);
        Assert.AreEqual(15, difficulty.MaxIncline);
    }

    [TestMethod]
    [DataRow("blue-pools-nz", 2)]
    [DataRow("norvege", 1)]
    public void Shipped_programs_keep_their_level(string id, int level)
    {
        Assert.AreEqual(level, ProgramDifficulty.Of(WwwrootHttpHandler.ShippedProgram(id)).Level);
    }
}
