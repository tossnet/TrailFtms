using TrailFtms.Services.Bluetooth;

namespace TrailFtms.Tests.Services.Bluetooth;

[TestClass]
public sealed class SupportedRangeTests
{
    private const double Tolerance = 1e-9;

    private static readonly SupportedRange Run18Speed = new(1.0, 18.0, 0.1);
    private static readonly SupportedRange Run18Incline = new(0.0, 15.0, 1.0);

    [TestMethod]
    [DataRow(5.0, 5.0)]
    [DataRow(5.04, 5.0)]
    [DataRow(5.05, 5.1)]
    [DataRow(0.5, 1.0)]
    [DataRow(25.0, 18.0)]
    public void Speed_is_clamped_and_snapped_to_step(double value, double expected)
    {
        Assert.AreEqual(expected, Run18Speed.Clamp(value), Tolerance);
    }

    [TestMethod]
    [DataRow(2.4, 2.0)]
    [DataRow(2.5, 3.0)]
    [DataRow(-2.0, 0.0)]
    [DataRow(16.0, 15.0)]
    public void Incline_is_rounded_to_whole_percent_on_run18(double value, double expected)
    {
        Assert.AreEqual(expected, Run18Incline.Clamp(value), Tolerance);
    }

    [TestMethod]
    public void Snapping_never_exceeds_max()
    {
        var range = new SupportedRange(0.0, 10.0, 3.0); // steps: 0, 3, 6, 9

        Assert.AreEqual(9.0, range.Clamp(10.0), Tolerance);
    }

    [TestMethod]
    public void Zero_step_only_clamps()
    {
        var range = new SupportedRange(1.0, 18.0, 0.0);

        Assert.AreEqual(4.37, range.Clamp(4.37), Tolerance);
    }

    [TestMethod]
    public void NaN_falls_back_to_min()
    {
        Assert.AreEqual(1.0, Run18Speed.Clamp(double.NaN), Tolerance);
    }
}
