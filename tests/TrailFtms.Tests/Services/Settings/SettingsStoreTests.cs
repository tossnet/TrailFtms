using TrailFtms.Services.Bluetooth;
using TrailFtms.Services.Ride;
using TrailFtms.Services.Settings;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Services.Settings;

[TestClass]
public sealed class SettingsStoreTests
{
    private const string SettingsKey = "trailftms.settings";
    private const string SafetyKey = "trailftms.safety-accepted";

    private static (FakeBrowserStorage Storage, FakeFtmsJs Ftms, SettingsStore Store) Create()
    {
        var storage = new FakeBrowserStorage();
        var ftms = new FakeFtmsJs();
        return (storage, ftms, new SettingsStore(storage, ftms.Treadmill));
    }

    [TestMethod]
    public async Task Defaults_when_nothing_is_stored()
    {
        var (_, ftms, store) = Create();

        await store.LoadAsync();

        Assert.AreEqual(new AppSettings(), store.Current);
        Assert.IsFalse(store.SafetyAccepted);
        Assert.IsNull(ftms.Treadmill.Limits.MaxSpeed);
    }

    [TestMethod]
    public async Task Stored_settings_are_restored_and_limits_applied()
    {
        var (storage, ftms, store) = Create();
        storage.Items[SettingsKey] = """{"ride":{"speedFactor":0.9,"pauseBehavior":"PauseTreadmill","pauseSpeed":2.5},"maxSpeed":8,"maxIncline":6}""";
        storage.Items[SafetyKey] = "true";

        await store.LoadAsync();

        Assert.AreEqual(0.9, store.Current.Ride.SpeedFactor);
        Assert.AreEqual(PauseBehavior.PauseTreadmill, store.Current.Ride.PauseBehavior);
        Assert.IsTrue(store.SafetyAccepted);
        Assert.AreEqual(new TreadmillLimits(8, 6), ftms.Treadmill.Limits);
    }

    [TestMethod]
    public async Task Corrupted_settings_fall_back_to_defaults()
    {
        var (storage, _, store) = Create();
        storage.Items[SettingsKey] = "{oops";

        await store.LoadAsync();

        Assert.AreEqual(new AppSettings(), store.Current);
    }

    [TestMethod]
    public async Task Save_persists_applies_limits_and_notifies()
    {
        var (storage, ftms, store) = Create();
        await store.LoadAsync();
        var changes = 0;
        store.Changed += () => changes++;

        await store.SaveAsync(new AppSettings { MaxSpeed = 7.5 });

        Assert.AreEqual(7.5, ftms.Treadmill.Limits.MaxSpeed);
        Assert.AreEqual(1, changes);
        StringAssert.Contains(storage.Items[SettingsKey], "\"maxSpeed\":7.5");
        StringAssert.Contains(storage.Items[SettingsKey], "\"pauseBehavior\":\"SlowDown\"");
    }

    [TestMethod]
    public async Task Accepting_safety_is_remembered()
    {
        var (storage, _, store) = Create();

        await store.AcceptSafetyAsync();

        Assert.IsTrue(store.SafetyAccepted);
        Assert.AreEqual("true", storage.Items[SafetyKey]);
    }
}
