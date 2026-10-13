using Bunit;
using TrailFtms.Services.Bluetooth;
using TrailFtms.Services.Ride;
using TrailFtms.Services.Settings;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Services.Settings;

[TestClass]
public sealed class SettingsStoreTests
{
    private static (BunitContext Ctx, FakeFtmsJs Ftms, SettingsStore Store) Create(string? settings = null, string? safety = null)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Setup<string?>("localStorage.getItem", "trailftms.settings").SetResult(settings);
        ctx.JSInterop.Setup<string?>("localStorage.getItem", "trailftms.safety-accepted").SetResult(safety);
        ctx.JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();
        var ftms = new FakeFtmsJs();
        return (ctx, ftms, new SettingsStore(ctx.JSInterop.JSRuntime, ftms.Treadmill));
    }

    [TestMethod]
    public async Task Defaults_when_nothing_is_stored()
    {
        var (ctx, ftms, store) = Create();
        using var _ = ctx;

        await store.LoadAsync();

        Assert.AreEqual(new AppSettings(), store.Current);
        Assert.IsFalse(store.SafetyAccepted);
        Assert.IsNull(ftms.Treadmill.Limits.MaxSpeed);
    }

    [TestMethod]
    public async Task Stored_settings_are_restored_and_limits_applied()
    {
        var json = """{"ride":{"speedFactor":0.9,"pauseBehavior":"PauseTreadmill","pauseSpeed":2.5},"maxSpeed":8,"maxIncline":6}""";
        var (ctx, ftms, store) = Create(json, "true");
        using var _ = ctx;

        await store.LoadAsync();

        Assert.AreEqual(0.9, store.Current.Ride.SpeedFactor);
        Assert.AreEqual(PauseBehavior.PauseTreadmill, store.Current.Ride.PauseBehavior);
        Assert.IsTrue(store.SafetyAccepted);
        Assert.AreEqual(new TreadmillLimits(8, 6), ftms.Treadmill.Limits);
    }

    [TestMethod]
    public async Task Corrupted_settings_fall_back_to_defaults()
    {
        var (ctx, _, store) = Create("{oops");
        using var __ = ctx;

        await store.LoadAsync();

        Assert.AreEqual(new AppSettings(), store.Current);
    }

    [TestMethod]
    public async Task Save_persists_applies_limits_and_notifies()
    {
        var (ctx, ftms, store) = Create();
        using var _ = ctx;
        await store.LoadAsync();
        var changes = 0;
        store.Changed += () => changes++;

        await store.SaveAsync(new AppSettings { MaxSpeed = 7.5 });

        Assert.AreEqual(7.5, ftms.Treadmill.Limits.MaxSpeed);
        Assert.AreEqual(1, changes);
        var saved = ctx.JSInterop.VerifyInvoke("localStorage.setItem");
        Assert.AreEqual("trailftms.settings", saved.Arguments[0]);
        StringAssert.Contains((string)saved.Arguments[1]!, "\"maxSpeed\":7.5");
        StringAssert.Contains((string)saved.Arguments[1]!, "\"pauseBehavior\":\"SlowDown\"");
    }

    [TestMethod]
    public async Task Accepting_safety_is_remembered()
    {
        var (ctx, _, store) = Create();
        using var __ = ctx;

        await store.AcceptSafetyAsync();

        Assert.IsTrue(store.SafetyAccepted);
        var saved = ctx.JSInterop.VerifyInvoke("localStorage.setItem");
        Assert.AreEqual("trailftms.safety-accepted", saved.Arguments[0]);
    }
}
