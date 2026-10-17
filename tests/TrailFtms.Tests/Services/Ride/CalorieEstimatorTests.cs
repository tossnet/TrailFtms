using TrailFtms.Services.Bluetooth;
using TrailFtms.Services.Ride;

namespace TrailFtms.Tests.Services.Ride;

[TestClass]
public sealed class CalorieEstimatorTests
{
    private static readonly BodyProfile Profile = new() { WeightKg = 70, Age = 40 };

    [TestMethod]
    [DataRow(6.0, 0.0, 4.725, DisplayName = "walking, flat")]
    [DataRow(5.0, 10.0, 9.392, DisplayName = "walking uphill")]
    [DataRow(10.0, 0.0, 12.892, DisplayName = "running, flat")]
    [DataRow(10.0, -5.0, 12.892, DisplayName = "downhill counts as flat")]
    public void Acsm_equations_follow_speed_and_incline(double speed, double incline, double expected)
    {
        Assert.AreEqual(expected, CalorieEstimator.KcalPerMinute(Profile, speed, incline, heartRate: null), 0.001);
    }

    [TestMethod]
    public void Rate_scales_with_weight()
    {
        var light = CalorieEstimator.KcalPerMinute(Profile, 6, 0, null);
        var heavy = CalorieEstimator.KcalPerMinute(Profile with { WeightKg = 105 }, 6, 0, null);

        Assert.AreEqual(light * 1.5, heavy, 0.001);
    }

    [TestMethod]
    public void Stopped_belt_burns_nothing()
    {
        Assert.AreEqual(0, CalorieEstimator.KcalPerMinute(Profile, 0, 5, 120));
    }

    [TestMethod]
    public void Heart_rate_is_used_once_the_sex_is_known()
    {
        Assert.AreEqual(4.725, CalorieEstimator.KcalPerMinute(Profile, 6, 0, 140), 0.001, "sex unknown: ACSM");
        Assert.AreEqual(13.196, CalorieEstimator.KcalPerMinute(Profile with { Sex = Sex.Male }, 6, 0, 140), 0.001);
        Assert.AreEqual(8.682, CalorieEstimator.KcalPerMinute(Profile with { Sex = Sex.Female }, 6, 0, 140), 0.001);
    }

    [TestMethod]
    public void Energy_is_integrated_over_time()
    {
        var estimator = new CalorieEstimator { Profile = Profile };
        var data = new TreadmillData { SpeedKmh = 10, InclinePercent = 0 };

        for (var t = 0; t <= 60; t++)
            estimator.Observe(t, data);

        Assert.AreEqual(12.892, estimator.TotalKcal, 0.001);
    }

    [TestMethod]
    public void Long_silence_is_not_extrapolated()
    {
        var estimator = new CalorieEstimator { Profile = Profile };
        var data = new TreadmillData { SpeedKmh = 10, InclinePercent = 0 };

        estimator.Observe(0, data);
        estimator.Observe(600, data);

        Assert.AreEqual(12.892 * 5 / 60, estimator.TotalKcal, 0.001);
    }

    [TestMethod]
    public void Nothing_is_counted_without_profile()
    {
        var estimator = new CalorieEstimator();
        var data = new TreadmillData { SpeedKmh = 10 };

        estimator.Observe(0, data);
        estimator.Observe(1, data);

        Assert.AreEqual(0, estimator.TotalKcal);
    }
}
