using Bunit;
using TrailFtms.Components;
using TrailFtms.Services.Programs;

namespace TrailFtms.Tests.Components;

[TestClass]
public sealed class ProgramTimelineTests
{
    private static readonly RideProgram Program = new()
    {
        Id = "test",
        Title = "Test",
        YoutubeId = "KiEN4HxM4ic",
        Segments = [new(0, 3, 0), new(250, 3, 10), new(500, 3, 5)],
    };

    [TestMethod]
    public void Draws_a_step_profile_over_the_duration()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ProgramTimeline>(p => p
            .Add(c => c.Program, Program)
            .Add(c => c.Duration, 1000)
            .Add(c => c.Position, 500));

        // Incline 0..10 mapped to y 94..6: 0 % at the bottom, 10 % at the top.
        Assert.AreEqual("M0,94 V94 H250 V6 H500 V50 H1000", cut.Find("path.line").GetAttribute("d"));
        Assert.AreEqual("500", cut.Find("line.cursor").GetAttribute("x1"));
        Assert.HasCount(3, cut.FindAll("rect.hit"));
    }

    [TestMethod]
    public void Tapping_a_segment_seeks_to_its_start()
    {
        using var ctx = new BunitContext();
        double? sought = null;
        var cut = ctx.Render<ProgramTimeline>(p => p
            .Add(c => c.Program, Program)
            .Add(c => c.Duration, 1000)
            .Add(c => c.OnSeek, t => sought = t));

        cut.FindAll("rect.hit")[1].Click();

        Assert.AreEqual(250, sought);
    }

    [TestMethod]
    public void Unknown_duration_uses_the_last_segment_plus_a_minute()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ProgramTimeline>(p => p
            .Add(c => c.Program, Program)
            .Add(c => c.Position, 560));

        Assert.AreEqual("1000", cut.Find("line.cursor").GetAttribute("x1")); // 560 / (500 + 60)
    }
}
