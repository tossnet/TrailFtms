using TrailFtms.Services.Programs;
using TrailFtms.Services.Ride;

namespace TrailFtms.Tests.Services.Ride;

[TestClass]
public sealed class RideAdjustmentTests
{
    private static readonly RideProgram Program = new()
    {
        Id = "test",
        Title = "Test",
        YoutubeId = "KiEN4HxM4ic",
        DurationSeconds = 600,
        Segments = [new(0, 1.0, 0), new(40, 3.0, 3), new(50, 3.0, 1), new(60, 3.2, 0)],
    };

    private static ProgramSegment[] Apply(params RideAdjustment[] adjustments) =>
        RideAdjustment.ApplyAll(Program, adjustments).Segments.ToArray();

    [TestMethod]
    public void A_change_becomes_a_point_and_lasts_until_the_program_changes_that_value()
    {
        // Speed 3.5 from 45.6 s: the treadmill kept it at 50 s (incline-only change), not at 60 s.
        var segments = Apply(new RideAdjustment(45.6, AdjustedValue.Speed, 3.5));

        CollectionAssert.AreEqual(
            new ProgramSegment[] { new(0, 1.0, 0), new(40, 3.0, 3), new(45, 3.5, 3), new(50, 3.5, 1), new(60, 3.2, 0) },
            segments);
    }

    [TestMethod]
    public void An_incline_change_keeps_the_speed()
    {
        var segments = Apply(new RideAdjustment(10, AdjustedValue.Incline, 2));

        CollectionAssert.AreEqual(
            new ProgramSegment[] { new(0, 1.0, 0), new(10, 1.0, 2), new(40, 3.0, 3), new(50, 3.0, 1), new(60, 3.2, 0) },
            segments);
    }

    [TestMethod]
    public void A_change_at_a_point_replaces_it()
    {
        var segments = Apply(new RideAdjustment(40, AdjustedValue.Incline, 1));

        // 40 s now matches 50 s: the repeated point is dropped.
        CollectionAssert.AreEqual(
            new ProgramSegment[] { new(0, 1.0, 0), new(40, 3.0, 1), new(60, 3.2, 0) },
            segments);
    }

    [TestMethod]
    public void A_later_change_wins()
    {
        var segments = Apply(
            new RideAdjustment(10, AdjustedValue.Speed, 2.0),
            new RideAdjustment(10, AdjustedValue.Speed, 1.5));

        Assert.AreEqual(new ProgramSegment(10, 1.5, 0), segments[1]);
    }
}
