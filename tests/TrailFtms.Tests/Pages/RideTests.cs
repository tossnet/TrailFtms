using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TrailFtms.Components;
using TrailFtms.Pages;
using TrailFtms.Services.Video;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Pages;

[TestClass]
public sealed class RideTests
{
    [TestMethod]
    public void Video_from_query_string_is_loaded()
    {
        using var ctx = new AppTestContext();
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("ride?v=https%3A%2F%2Fyoutu.be%2FKiEN4HxM4ic");

        var cut = ctx.Render<Ride>();

        Assert.HasCount(1, cut.FindComponents<VideoPlayer>());
        CollectionAssert.AreEqual(new[] { "KiEN4HxM4ic" }, ctx.Video.Loads);
    }

    [TestMethod]
    public void Invalid_link_shows_message_and_loads_nothing()
    {
        using var ctx = new AppTestContext();
        var cut = ctx.Render<Ride>();

        cut.Find("input").Input("https://vimeo.com/123");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Charger")).Click();

        StringAssert.Contains(cut.Markup, "Lien YouTube non reconnu.");
        Assert.IsEmpty(cut.FindComponents<VideoPlayer>());
    }

    [TestMethod]
    public void Valid_link_loads_video_and_updates_url()
    {
        using var ctx = new AppTestContext();
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("ride");
        var cut = ctx.Render<Ride>();

        cut.Find("input").Input("https://www.youtube.com/watch?v=KiEN4HxM4ic");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Charger")).Click();

        CollectionAssert.AreEqual(new[] { "KiEN4HxM4ic" }, ctx.Video.Loads);
        StringAssert.EndsWith(ctx.Services.GetRequiredService<NavigationManager>().Uri, "ride?v=KiEN4HxM4ic");
    }

    [TestMethod]
    public async Task Player_state_and_errors_are_displayed()
    {
        using var ctx = new AppTestContext();
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("ride?v=KiEN4HxM4ic");
        var cut = ctx.Render<Ride>();

        await cut.InvokeAsync(() => ctx.Video.SetState(VideoState.Playing));
        StringAssert.Contains(cut.Markup, "Lecture");

        await cut.InvokeAsync(() => ctx.Video.RaiseError(VideoError.NotEmbeddable));
        StringAssert.Contains(cut.Markup, "interdit sa lecture en dehors de YouTube");
    }

    [TestMethod]
    public async Task Leaving_the_page_unloads_the_player()
    {
        var ctx = new AppTestContext();
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("ride?v=KiEN4HxM4ic");
        ctx.Render<Ride>();

        await ctx.DisposeAsync(); // disposes the rendered components

        Assert.AreEqual(VideoState.NotLoaded, ctx.Video.State);
    }
}
