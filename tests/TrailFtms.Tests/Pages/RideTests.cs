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

    [TestMethod]
    public void Program_from_query_string_loads_its_video()
    {
        using var ctx = new AppTestContext();
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("ride?program=blue-pools-nz");

        var cut = ctx.Render<Ride>();

        cut.WaitForAssertion(() => CollectionAssert.AreEqual(new[] { "KiEN4HxM4ic" }, ctx.Video.Loads));
        Assert.AreEqual("Blue Pools, Nouvelle-Zélande", cut.Find(".panel-head .title").TextContent);
        Assert.HasCount(1, cut.FindAll(".timeline"));
        Assert.IsEmpty(cut.FindAll("input"));
    }

    [TestMethod]
    public void Unknown_program_shows_message()
    {
        using var ctx = new AppTestContext();
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("ride?program=nope");

        var cut = ctx.Render<Ride>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Programme introuvable."));
        Assert.IsEmpty(ctx.Video.Loads);
    }

    private static async Task<(AppTestContext Ctx, IRenderedComponent<Ride> Cut)> RenderProgramAsync(bool connected)
    {
        var ctx = new AppTestContext();
        if (connected)
            await ctx.Ftms.Treadmill.ConnectAsync();
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("ride?program=blue-pools-nz");
        var cut = ctx.Render<Ride>();
        cut.WaitForAssertion(() => Assert.HasCount(1, ctx.Video.Loads));
        return (ctx, cut);
    }

    [TestMethod]
    public async Task Without_treadmill_the_video_plays_alone()
    {
        var (ctx, cut) = await RenderProgramAsync(connected: false);
        using var _ = ctx;

        StringAssert.Contains(cut.Markup, "Tapis non connecté");
        Assert.IsTrue(cut.FindAll("button").Any(b => b.TextContent.Trim() == "STOP"), "STOP is always visible");
        Assert.IsEmpty(cut.FindAll(".target"));
    }

    [TestMethod]
    public async Task Connected_treadmill_follows_the_video()
    {
        var (ctx, cut) = await RenderProgramAsync(connected: true);
        using var _ = ctx;
        ctx.Ftms.Writes.Clear();

        ctx.Video.CurrentTime = 44; // Blue Pools: 3.0 km/h, 3 % from 0:40 to 0:50 (1 % sent 5 s early)
        await cut.InvokeAsync(() => ctx.Video.SetState(VideoState.Playing));

        cut.WaitForAssertion(() => Assert.HasCount(2, cut.FindAll(".target").Take(2).Where(t => t.TextContent.StartsWith("Consigne"))));
        var targets = cut.FindAll(".target").Select(t => t.TextContent).ToList();
        Assert.AreEqual("Consigne 3,0", targets[0]);
        Assert.AreEqual("Consigne 3", targets[1]);
        Assert.AreEqual("dans 0:06", targets[2]); // next: 1 % at 0:50
        StringAssert.Contains(cut.Find(".metric.next .value").TextContent, "1 %");
        StringAssert.Contains(cut.Markup, "Synchro active");
        CollectionAssert.AreEqual(new byte[] { 0x07 }, ctx.Ftms.Writes[0]);
    }

    [TestMethod]
    public async Task Stop_button_halts_and_sync_can_be_resumed()
    {
        var (ctx, cut) = await RenderProgramAsync(connected: true);
        using var _ = ctx;
        ctx.Video.CurrentTime = 45;
        await cut.InvokeAsync(() => ctx.Video.SetState(VideoState.Playing));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "STOP").Click();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Arrêt demandé."));
        CollectionAssert.AreEqual(new byte[] { 0x08, 0x01 }, ctx.Ftms.Writes[^1]);
        Assert.AreEqual(VideoState.Paused, ctx.Video.State);

        cut.FindAll("button").Single(b => b.TextContent.Contains("Relancer la synchro")).Click();
        cut.WaitForAssertion(() => StringAssert.DoesNotMatch(cut.Markup, new System.Text.RegularExpressions.Regex("Arrêt demandé")));
    }

    [TestMethod]
    public async Task Connection_loss_during_the_ride_is_shown()
    {
        var (ctx, cut) = await RenderProgramAsync(connected: true);
        using var _ = ctx;

        await cut.InvokeAsync(ctx.Ftms.Treadmill.OnDisconnected);

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Connexion au tapis perdue"));
        Assert.IsFalse(cut.FindAll("button").Any(b => b.TextContent.Contains("Relancer la synchro")));
    }

    [TestMethod]
    public async Task Panel_only_shows_speed_incline_and_next_change()
    {
        var (ctx, cut) = await RenderProgramAsync(connected: true);
        using var _ = ctx;
        ctx.Video.CurrentTime = 45;
        await cut.InvokeAsync(() => ctx.Video.SetState(VideoState.Playing));

        cut.WaitForAssertion(() => Assert.HasCount(3, cut.FindAll(".metric")));
        CollectionAssert.AreEqual(new[] { "Vitesse", "Inclinaison", "Ensuite" },
            cut.FindAll(".metric .label").Select(l => l.TextContent).ToArray());
        Assert.IsEmpty(cut.FindAll(".pace-value"));
    }

    [TestMethod]
    public async Task Speed_factor_from_settings_applies_during_the_ride()
    {
        var (ctx, cut) = await RenderProgramAsync(connected: true);
        using var _ = ctx;
        ctx.Video.CurrentTime = 45;
        await cut.InvokeAsync(() => ctx.Video.SetState(VideoState.Playing));
        cut.WaitForAssertion(() => Assert.AreEqual("Consigne 3,0", cut.FindAll(".target")[0].TextContent));

        var settings = ctx.Services.GetRequiredService<TrailFtms.Services.Settings.SettingsStore>();
        await cut.InvokeAsync(() => settings.SaveAsync(settings.Current with { Ride = settings.Current.Ride with { SpeedFactor = 1.1 } }));
        ctx.Time.Advance(TimeSpan.FromMilliseconds(250)); // next engine tick applies the new factor

        cut.WaitForAssertion(() => Assert.AreEqual("Consigne 3,3", cut.FindAll(".target")[0].TextContent));
    }

    [TestMethod]
    public async Task Long_video_profile_scrolls_with_the_position()
    {
        var (ctx, cut) = await RenderProgramAsync(connected: false); // Blue Pools: 51:52
        using var _ = ctx;
        ctx.Video.SetState(VideoState.Playing);

        ctx.Video.CurrentTime = 45;
        ctx.Time.Advance(TimeSpan.FromMilliseconds(250));
        cut.WaitForAssertion(() => CollectionAssert.AreEqual(new[] { "0:00", "5:00" }, cut.FindAll(".edge").Select(e => e.TextContent).ToArray()));

        ctx.Video.CurrentTime = 20 * 60;
        ctx.Time.Advance(TimeSpan.FromMilliseconds(250));
        cut.WaitForAssertion(() => CollectionAssert.AreEqual(new[] { "19:00", "24:00" }, cut.FindAll(".edge").Select(e => e.TextContent).ToArray()));
    }

    [TestMethod]
    public async Task Keyboard_shortcut_stops_belt_and_video()
    {
        var (ctx, cut) = await RenderProgramAsync(connected: true);
        using var _ = ctx;
        ctx.Video.CurrentTime = 45;
        await cut.InvokeAsync(() => ctx.Video.SetState(VideoState.Playing));

        await cut.InvokeAsync(() => cut.FindComponent<StopShortcut>().Instance.OnStopShortcut());

        CollectionAssert.AreEqual(new byte[] { 0x08, 0x01 }, ctx.Ftms.Writes[^1]);
        Assert.AreEqual(VideoState.Paused, ctx.Video.State);
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Arrêt demandé."));
    }

    [TestMethod]
    public async Task Stop_works_even_without_a_running_sync()
    {
        using var ctx = new AppTestContext();
        await ctx.Ftms.Treadmill.ConnectAsync();
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("ride?v=KiEN4HxM4ic");
        var cut = ctx.Render<Ride>();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "STOP").Click();

        CollectionAssert.AreEqual(new byte[] { 0x08, 0x01 }, ctx.Ftms.Writes[^1]);
    }
}
