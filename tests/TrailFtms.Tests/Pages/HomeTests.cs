using Bunit;
using TrailFtms.Components;
using TrailFtms.Pages;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Pages;

[TestClass]
public sealed class HomeTests
{
    [TestMethod]
    public void Renders_app_title_and_connect_button()
    {
        using var ctx = new AppTestContext();

        var cut = ctx.Render<Home>();

        Assert.AreEqual("TrailFtms", cut.Find(".hd-1").TextContent);
        var connect = cut.FindAll("button").Single(b => b.TextContent.Contains("Connecter le tapis"));
        Assert.IsFalse(connect.HasAttribute("disabled"));
        Assert.IsEmpty(cut.FindComponents<TreadmillPanel>());
    }

    [TestMethod]
    public void Unsupported_browser_shows_message_and_disables_connect()
    {
        using var ctx = new AppTestContext();
        ctx.Ftms.Availability = "unsupported";

        var cut = ctx.Render<Home>();

        StringAssert.Contains(cut.Markup, "Ce navigateur ne gère pas le Bluetooth");
        Assert.IsTrue(cut.FindAll("button").Single(b => b.TextContent.Contains("Connecter")).HasAttribute("disabled"));
    }

    [TestMethod]
    public void Connecting_shows_capabilities_and_panel()
    {
        using var ctx = new AppTestContext();
        var cut = ctx.Render<Home>();

        cut.FindAll("button").Single(b => b.TextContent.Contains("Connecter le tapis")).Click();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Connecté à FS-TEST"));
        StringAssert.Contains(cut.Markup, "1,0 à 18,0 km/h");
        StringAssert.Contains(cut.Markup, "0 à 15 %");
        Assert.HasCount(1, cut.FindComponents<TreadmillPanel>());
    }

    [TestMethod]
    public async Task Connection_loss_shows_alert_and_reconnect()
    {
        using var ctx = new AppTestContext();
        await ctx.Ftms.Treadmill.ConnectAsync();
        var cut = ctx.Render<Home>();

        await cut.InvokeAsync(ctx.Ftms.Treadmill.OnDisconnected);

        StringAssert.Contains(cut.Markup, "Connexion perdue");
        Assert.IsTrue(cut.FindAll("button").Any(b => b.TextContent.Contains("Reconnecter")));
        Assert.IsEmpty(cut.FindComponents<TreadmillPanel>());
    }
}
