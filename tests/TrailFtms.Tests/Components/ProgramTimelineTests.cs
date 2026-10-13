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

        // Incline 0..10 mapped to y 94..20: 0 % at the bottom, 10 % at the top (room above for the legend).
        Assert.AreEqual("M0,94 V94 H250 V20 H500 V57 H1000", cut.Find("path.line").GetAttribute("d"));
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
    public void Window_shows_a_scrolling_part_of_the_route()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ProgramTimeline>(p => p
            .Add(c => c.Program, Program)
            .Add(c => c.Duration, 1000)
            .Add(c => c.Window, 200)
            .Add(c => c.Position, 500));

        // Window 460..660 (cursor at 20 %): end of the 10 % segment, then 5 % from 500 (x = 200).
        Assert.AreEqual("M0,20 V20 H200 V57 H1000", cut.Find("path.line").GetAttribute("d"));
        Assert.AreEqual("200", cut.Find("line.cursor").GetAttribute("x1"));
        CollectionAssert.AreEqual(new[] { "7:40", "11:00" }, cut.FindAll(".edge").Select(e => e.TextContent).ToArray());
        Assert.HasCount(2, cut.FindAll("rect.hit"));
    }

    [TestMethod]
    public void Window_stays_inside_the_route()
    {
        using var ctx = new BunitContext();

        var start = ctx.Render<ProgramTimeline>(p => p
            .Add(c => c.Program, Program).Add(c => c.Duration, 1000).Add(c => c.Window, 200).Add(c => c.Position, 10));
        var end = ctx.Render<ProgramTimeline>(p => p
            .Add(c => c.Program, Program).Add(c => c.Duration, 1000).Add(c => c.Window, 200).Add(c => c.Position, 990));

        CollectionAssert.AreEqual(new[] { "0:00", "3:20" }, start.FindAll(".edge").Select(e => e.TextContent).ToArray());
        CollectionAssert.AreEqual(new[] { "13:20", "16:40" }, end.FindAll(".edge").Select(e => e.TextContent).ToArray());
        Assert.AreEqual("950", end.Find("line.cursor").GetAttribute("x1"));
    }

    [TestMethod]
    public void Window_longer_than_the_route_shows_everything()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ProgramTimeline>(p => p
            .Add(c => c.Program, Program).Add(c => c.Duration, 1000).Add(c => c.Window, 5000));

        Assert.AreEqual("M0,94 V94 H250 V20 H500 V57 H1000", cut.Find("path.line").GetAttribute("d"));
        Assert.IsEmpty(cut.FindAll(".edge"));
    }

    [TestMethod]
    public void Segments_are_colored_by_speed_band_with_legend_and_tooltip()
    {
        using var ctx = new BunitContext();
        var program = Program with { Segments = [new(0, 3.9, 0), new(100, 4, 2), new(200, 7.9, 4), new(300, 8, 6)] };

        var cut = ctx.Render<ProgramTimeline>(p => p.Add(c => c.Program, program).Add(c => c.Duration, 400));

        CollectionAssert.AreEqual(
            new[] { "fill speed-0", "fill speed-1", "fill speed-2", "fill speed-3" },
            cut.FindAll("rect.fill").Select(r => r.ClassName).ToArray());
        Assert.AreEqual("1:40 · 2 % · 4,0 km/h", cut.FindAll("rect.hit title")[1].TextContent);
        CollectionAssert.AreEqual(new[] { "< 4", "4-6", "6-8", "8+ km/h" },
            cut.FindAll(".legend-item").Select(i => i.TextContent).ToArray());
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
