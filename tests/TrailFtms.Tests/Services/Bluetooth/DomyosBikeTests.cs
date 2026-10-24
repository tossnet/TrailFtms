using TrailFtms.Services.Bluetooth;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Services.Bluetooth;

/// <summary><see cref="FtmsTreadmill"/> connected to a Domyos bike.</summary>
[TestClass]
public sealed class DomyosBikeTests
{
    private static async Task<FakeFtmsJs> ConnectedAsync()
    {
        var js = new FakeFtmsJs { DeviceName = "DOMYOS-BIKE-1810", Protocol = "domyos" };
        await js.Treadmill.ConnectAsync();
        js.Writes.Clear();
        return js;
    }

    [TestMethod]
    public async Task Connect_sends_the_init_sequence()
    {
        var js = new FakeFtmsJs { DeviceName = "DOMYOS-BIKE-1810", Protocol = "domyos" };

        Assert.IsTrue(await js.Treadmill.ConnectAsync());

        var bike = js.Treadmill;
        Assert.AreEqual(TreadmillState.Connected, bike.State);
        Assert.AreEqual(MachineKind.Bike, bike.Kind);
        Assert.IsNull(bike.SpeedRange);
        Assert.AreEqual(DomyosProtocol.SimulatedInclineRange, bike.InclineRange);
        Assert.HasCount(DomyosProtocol.Init.Count, js.Writes);
        CollectionAssert.AreEqual(DomyosProtocol.Init[0], js.Writes[0]);
    }

    [TestMethod]
    public async Task Init_goes_on_when_the_console_does_not_answer()
    {
        var js = new FakeFtmsJs { DeviceName = "DOMYOS-BIKE-1810", Protocol = "domyos", Respond = _ => null };

        Assert.IsTrue(await js.Treadmill.ConnectAsync());
        Assert.HasCount(DomyosProtocol.Init.Count, js.Writes);
    }

    [TestMethod]
    public async Task Incline_is_simulated_by_the_resistance()
    {
        var js = await ConnectedAsync();
        js.Treadmill.Limits = new TreadmillLimits(MaxIncline: 8);

        Assert.AreEqual(ControlPointResult.Success, await js.Treadmill.SetInclineAsync(4.4));
        await js.Treadmill.SetInclineAsync(12);

        CollectionAssert.AreEqual(DomyosProtocol.SetResistance(5), js.Writes[0]);
        CollectionAssert.AreEqual(DomyosProtocol.SetResistance(9), js.Writes[1]); // user ceiling 8 %
    }

    [TestMethod]
    public async Task Speed_is_not_supported_and_start_or_pause_send_nothing()
    {
        var js = await ConnectedAsync();

        Assert.AreEqual(ControlPointResult.NotSupported, await js.Treadmill.SetSpeedAsync(20));
        Assert.AreEqual(ControlPointResult.Success, await js.Treadmill.RequestControlAsync());
        Assert.AreEqual(ControlPointResult.Success, await js.Treadmill.StartAsync());
        Assert.AreEqual(ControlPointResult.Success, await js.Treadmill.StopAsync(StopKind.Pause));

        Assert.IsEmpty(js.Writes);
    }

    [TestMethod]
    public async Task Stop_sets_the_lowest_resistance()
    {
        var js = await ConnectedAsync();

        await js.Treadmill.StopAsync();

        CollectionAssert.AreEqual(DomyosProtocol.SetResistance(DomyosProtocol.MinLevel), js.Writes.Single());
    }

    [TestMethod]
    public async Task Commands_need_a_connection()
    {
        var js = await ConnectedAsync();
        await js.Treadmill.DisconnectAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => js.Treadmill.StartAsync());
    }

    [TestMethod]
    public async Task Measurements_split_over_two_notifications_are_decoded()
    {
        var js = await ConnectedAsync();
        TreadmillData? received = null;
        js.Treadmill.DataReceived += d => received = d;
        var parameters = new byte[23];
        parameters.AsSpan().Fill(0xFF);
        parameters[4] = 0x00;
        parameters[5] = 0xC8; // 20.0 km/h
        parameters[7] = 85;   // rpm
        parameters[12] = 3;   // level
        parameters[16] = 0;   // no heart rate
        var frame = DomyosProtocol.Frame(0xBC, parameters);

        js.Treadmill.OnDomyosData(frame[..20]);
        Assert.IsNull(received);
        js.Treadmill.OnDomyosData(frame[20..]);

        Assert.AreEqual(20.0, received!.SpeedKmh!.Value, 1e-9);
        Assert.AreEqual(85, received.CadenceRpm);
        Assert.AreEqual(3, received.ResistanceLevel);
        Assert.AreEqual(2.0, received.InclinePercent!.Value, 1e-9);
    }
}
