using Bunit;
using TrailFtms.Components;
using TrailFtms.Services.Programs;

namespace TrailFtms.Tests.Components;

[TestClass]
public sealed class RouteProfileTests
{
    private static readonly RideProgram Program = new()
    {
        Id = "test",
        Title = "Test",
        YoutubeId = "KiEN4HxM4ic",
        Segments = [new(0, 3, 0), new(250, 3, 10), new(500, 3, 5)],
    };

    [TestMethod]
    public void Draws_the_whole_route_from_the_start_flag_to_the_finish_flag()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<RouteProfile>(p => p
            .Add(c => c.Program, Program)
            .Add(c => c.Duration, 1000)
            .Add(c => c.Position, 375));

        // x 6..284 over 0..1000 s, incline 0..10 mapped to y 86..26 (room above for the flags).
        Assert.AreEqual("6,86 40.75,86 110.25,26 214.5,56 284,56", cut.Find("polyline.curve").GetAttribute("points"));
        Assert.AreEqual("6", cut.Find(".flag.start .pole").GetAttribute("x1"));
        Assert.AreEqual("62", cut.Find(".flag.start .pole").GetAttribute("y2"));
        Assert.AreEqual("6,62 20,66.5 6,71", cut.Find(".flag.start .banner").GetAttribute("points"));
        Assert.AreEqual("284", cut.Find(".flag.finish .pole").GetAttribute("x1"));
        Assert.AreEqual("110.25", cut.Find("circle.position").GetAttribute("cx"));
        Assert.AreEqual("26", cut.Find("circle.position").GetAttribute("cy"));
    }

    [TestMethod]
    public void Draws_nothing_without_segments()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<RouteProfile>(p => p
            .Add(c => c.Program, Program with { Segments = [] })
            .Add(c => c.Duration, 1000));

        Assert.IsEmpty(cut.FindAll(".flag"));
    }
}
