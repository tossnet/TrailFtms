using TrailFtms.Services.Programs;

namespace TrailFtms.Tests.Services.Programs;

[TestClass]
public sealed class RideProgramTests
{
    private static readonly RideProgram Program = new()
    {
        Id = "test",
        Title = "Test",
        YoutubeId = "KiEN4HxM4ic",
        Segments = [new(0, 1, 0), new(40, 3, 3), new(50, 3, 1), new(60, 3.2, 0)],
    };

    [TestMethod]
    [DataRow(0.0, 0)]
    [DataRow(39.99, 0)]
    [DataRow(40.0, 1)]
    [DataRow(49.5, 1)]
    [DataRow(50.0, 2)]
    [DataRow(60.0, 3)]
    [DataRow(5000.0, 3)]
    [DataRow(-1.0, -1)]
    public void SegmentIndexAt_finds_the_active_segment(double seconds, int expected)
    {
        Assert.AreEqual(expected, Program.SegmentIndexAt(seconds));
    }

    [TestMethod]
    public void SegmentIndexAt_without_segments_is_minus_one()
    {
        Assert.AreEqual(-1, (Program with { Segments = [] }).SegmentIndexAt(10));
    }
}
