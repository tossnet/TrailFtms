using Bunit;
using TrailFtms.Components;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Components;

[TestClass]
public sealed class TreadmillPanelTests
{
    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    private static async Task<(AppTestContext Ctx, IRenderedComponent<TreadmillPanel> Cut)> RenderConnectedAsync()
    {
        var ctx = new AppTestContext();
        await ctx.Ftms.Treadmill.ConnectAsync();
        ctx.Ftms.Writes.Clear();
        return (ctx, ctx.Render<TreadmillPanel>());
    }

    private static void ClickButton(IRenderedComponent<TreadmillPanel> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

    [TestMethod]
    public async Task Speed_buttons_send_clamped_targets()
    {
        var (ctx, cut) = await RenderConnectedAsync();
        using var _ = ctx;

        ClickButton(cut, "+0,5"); // from range min 1.0 to 1.5
        ClickButton(cut, "-0,5");
        ClickButton(cut, "-0,5"); // stays at range min

        CollectionAssert.AreEqual(Hex("02 96 00"), ctx.Ftms.Writes[0]);
        CollectionAssert.AreEqual(Hex("02 64 00"), ctx.Ftms.Writes[1]);
        CollectionAssert.AreEqual(Hex("02 64 00"), ctx.Ftms.Writes[2]);
        StringAssert.Contains(cut.Markup, "1,0 km/h");
    }

    [TestMethod]
    public async Task Incline_buttons_use_machine_step()
    {
        var (ctx, cut) = await RenderConnectedAsync();
        using var _ = ctx;

        ClickButton(cut, "+1,0");

        CollectionAssert.AreEqual(Hex("03 0A 00"), ctx.Ftms.Writes.Single());
    }

    [TestMethod]
    public async Task Start_pause_and_stop_send_commands()
    {
        var (ctx, cut) = await RenderConnectedAsync();
        using var _ = ctx;

        ClickButton(cut, "Démarrer");
        ClickButton(cut, "Pause");
        ClickButton(cut, "STOP");

        CollectionAssert.AreEqual(Hex("07"), ctx.Ftms.Writes[0]);
        CollectionAssert.AreEqual(Hex("08 02"), ctx.Ftms.Writes[1]);
        CollectionAssert.AreEqual(Hex("08 01"), ctx.Ftms.Writes[2]);
    }

    [TestMethod]
    public async Task Rejected_command_shows_reason()
    {
        var (ctx, cut) = await RenderConnectedAsync();
        using var _ = ctx;
        ctx.Ftms.Respond = command => [0x80, command[0], 0x02];

        ClickButton(cut, "Démarrer");

        StringAssert.Contains(cut.Markup, "Commande refusée par le tapis : non supportée");
    }

    [TestMethod]
    public async Task Live_data_and_safety_key_are_displayed()
    {
        var (ctx, cut) = await RenderConnectedAsync();
        using var _ = ctx;

        await cut.InvokeAsync(() => ctx.Ftms.Treadmill.OnTreadmillData(Hex("8C 04 F4 01 2C 01 00 14 00 00 00 0F 00 FF FF FF 3C 00")));
        await cut.InvokeAsync(() => ctx.Ftms.Treadmill.OnMachineStatus(Hex("03")));

        var values = cut.FindAll(".metric-value").Select(v => v.TextContent).ToList();
        StringAssert.StartsWith(values[0], "5,0");
        StringAssert.StartsWith(values[1], "2");
        StringAssert.StartsWith(values[2], "300");
        Assert.AreEqual("0:01:00", values[3].Trim());
        StringAssert.Contains(cut.Markup, "Clé de sécurité retirée");
    }
}
