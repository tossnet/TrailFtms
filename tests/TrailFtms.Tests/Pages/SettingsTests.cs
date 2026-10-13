using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TrailFtms.Services.Ride;
using TrailFtms.Services.Settings;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Pages;

[TestClass]
public sealed class SettingsTests
{
    [TestMethod]
    public void Enabling_a_limit_saves_it_and_applies_it_to_the_treadmill()
    {
        using var ctx = new AppTestContext();
        var cut = ctx.Render<TrailFtms.Pages.Settings>();
        cut.WaitForAssertion(() => Assert.IsNotEmpty(cut.FindAll("input[type=checkbox]")));

        cut.FindAll("input[type=checkbox]")[0].Input(true); // WinOldCheckBox listens to "input"

        var store = ctx.Services.GetRequiredService<SettingsStore>();
        Assert.AreEqual(10, store.Current.MaxSpeed);
        Assert.AreEqual(10, ctx.Ftms.Treadmill.Limits.MaxSpeed);
        Assert.IsNull(store.Current.MaxIncline);
    }

    [TestMethod]
    public void Pause_behavior_can_be_chosen()
    {
        using var ctx = new AppTestContext();
        var cut = ctx.Render<TrailFtms.Pages.Settings>();
        cut.WaitForAssertion(() => Assert.IsNotEmpty(cut.FindAll("input[type=radio]")));

        cut.FindAll("input[type=radio]")[2].Change(true); // order of PauseBehavior: Nothing, SlowDown, PauseTreadmill

        Assert.AreEqual(PauseBehavior.PauseTreadmill, ctx.Services.GetRequiredService<SettingsStore>().Current.Ride.PauseBehavior);
    }
}
