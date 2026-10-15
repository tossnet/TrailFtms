using TrailFtms.Services.Programs;
using TrailFtms.Services.Ride;

namespace TrailFtms.Tests.Services.Ride;

[TestClass]
public sealed class RideRecorderTests
{
    private const double Duration = 600;

    private static RideRecorder Recorder() => new(speedStep: 0.1, inclineStep: 1);

    /// <summary>One measure every 250 ms of video, from <paramref name="from"/> (included) to <paramref name="to"/> (excluded).</summary>
    private static void Ride(RideRecorder recorder, double from, double to, double speed, double incline)
    {
        for (var t = from; t < to; t += 0.25)
            recorder.Observe(t, Duration, speed, incline);
    }

    [TestMethod]
    public void Nothing_is_recorded_until_speed_and_incline_are_stable()
    {
        var recorder = Recorder();

        Ride(recorder, 0, 2, 4.0, 0);

        Assert.IsFalse(recorder.HasData);
        Assert.IsEmpty(recorder.Segments);
    }

    [TestMethod]
    public void The_first_stable_values_start_the_program_at_0()
    {
        var recorder = Recorder();

        // Belt accelerating on the first seconds, then 4.0 km/h.
        Ride(recorder, 5, 6, 1.0, 0);
        Ride(recorder, 6, 7, 2.5, 0);
        Ride(recorder, 7, 20, 4.0, 0);

        CollectionAssert.AreEqual(new ProgramSegment[] { new(0, 4.0, 0) }, recorder.Segments.ToArray());
    }

    [TestMethod]
    public void A_change_is_dated_from_when_it_started_ramps_skipped()
    {
        var recorder = Recorder();
        Ride(recorder, 0, 30, 4.0, 0);

        Ride(recorder, 30, 31, 4.5, 0); // ramp
        Ride(recorder, 31, 60, 5.0, 0);
        Ride(recorder, 60, 61, 5.0, 1); // incline motor on its way
        Ride(recorder, 61, 90, 5.0, 2);

        CollectionAssert.AreEqual(
            new ProgramSegment[] { new(0, 4.0, 0), new(30, 5.0, 0), new(60, 5.0, 2) },
            recorder.Segments.ToArray());
    }

    [TestMethod]
    public void Overlapping_speed_and_incline_changes_keep_both()
    {
        var recorder = Recorder();
        Ride(recorder, 0, 30, 4.0, 0);

        // Speed from 30 s, still ramping when the incline settles at 31 s: the incline is recorded first.
        Ride(recorder, 30, 31, 4.5, 0);
        Ride(recorder, 31, 33, 4.8, 3);
        Ride(recorder, 33, 34, 4.9, 3);
        Ride(recorder, 34, 60, 5.0, 3);

        CollectionAssert.AreEqual(
            new ProgramSegment[] { new(0, 4.0, 0), new(30, 5.0, 0), new(31, 5.0, 3) },
            recorder.Segments.ToArray());
    }

    [TestMethod]
    public void A_seek_back_records_that_part_again()
    {
        var recorder = Recorder();
        Ride(recorder, 0, 30, 4.0, 0);
        Ride(recorder, 30, 60, 5.0, 0);
        Ride(recorder, 60, 90, 6.0, 0);

        Ride(recorder, 40, 70, 5.5, 0); // back to 0:40, now at 5.5 km/h

        CollectionAssert.AreEqual(
            new ProgramSegment[] { new(0, 4.0, 0), new(30, 5.0, 0), new(40, 5.5, 0) },
            recorder.Segments.ToArray());
    }

    [TestMethod]
    public void Ads_are_skipped()
    {
        var recorder = Recorder();
        Ride(recorder, 0, 30, 4.0, 0);

        for (var t = 0.0; t < 15; t += 0.25)
            recorder.Observe(t, 15, 3.0, 0); // 15 s ad: its time restarts at 0
        Ride(recorder, 30, 60, 4.0, 0);

        CollectionAssert.AreEqual(new ProgramSegment[] { new(0, 4.0, 0) }, recorder.Segments.ToArray());
    }

    [TestMethod]
    public void Every_change_increments_the_revision()
    {
        var recorder = Recorder();
        Ride(recorder, 0, 30, 4.0, 0);
        var revision = recorder.Revision;

        Ride(recorder, 30, 60, 4.0, 0);
        Assert.AreEqual(revision, recorder.Revision);

        Ride(recorder, 60, 90, 5.0, 0);
        Assert.AreEqual(revision + 1, recorder.Revision);
    }

    [TestMethod]
    public void The_recorded_program_is_valid_and_has_the_video_duration()
    {
        var recorder = Recorder();
        Ride(recorder, 0, 30, 4.0, 0);
        Ride(recorder, 30, 60, 5.2, 4);

        var program = recorder.ToProgram("blue-pools", "Blue Pools", "KiEN4HxM4ic");
        var parsed = ProgramParser.Parse(ProgramParser.Serialize(program));

        Assert.IsTrue(parsed.IsValid);
        Assert.AreEqual(600, parsed.Program!.DurationSeconds);
        CollectionAssert.AreEqual(recorder.Segments.ToArray(), parsed.Program.Segments.ToArray());
    }
}
