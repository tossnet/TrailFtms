using TrailFtms.Services.Bluetooth;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Services.Bluetooth;

[TestClass]
public sealed class FtmsTreadmillTests
{
    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    private static async Task<FakeFtmsJs> ConnectedAsync()
    {
        var js = new FakeFtmsJs();
        await js.Treadmill.ConnectAsync();
        js.Writes.Clear();
        return js;
    }

    [TestMethod]
    public async Task Connect_reads_capabilities_and_requests_control()
    {
        var js = new FakeFtmsJs();

        Assert.IsTrue(await js.Treadmill.ConnectAsync());

        var treadmill = js.Treadmill;
        Assert.AreEqual(TreadmillState.Connected, treadmill.State);
        Assert.AreEqual("FS-TEST", treadmill.DeviceName);
        Assert.IsTrue(treadmill.Features!.Value.CanSetSpeed);
        Assert.AreEqual(18.0, treadmill.SpeedRange!.Value.Max, 1e-9);
        Assert.AreEqual(15.0, treadmill.InclineRange!.Value.Max, 1e-9);
        CollectionAssert.AreEqual(Hex("00"), js.Writes.Single());
    }

    [TestMethod]
    public async Task Cancelled_picker_returns_false()
    {
        var js = new FakeFtmsJs { DeviceName = null };

        Assert.IsFalse(await js.Treadmill.ConnectAsync());
        Assert.AreEqual(TreadmillState.Disconnected, js.Treadmill.State);
    }

    [TestMethod]
    public async Task Targets_are_clamped_to_machine_ranges()
    {
        var js = await ConnectedAsync();

        await js.Treadmill.SetSpeedAsync(25);
        await js.Treadmill.SetInclineAsync(2.4);

        CollectionAssert.AreEqual(Hex("02 08 07"), js.Writes[0]); // 18.0 km/h
        CollectionAssert.AreEqual(Hex("03 14 00"), js.Writes[1]); // 2 %
    }

    [TestMethod]
    public async Task Control_not_permitted_requests_control_and_replays_once()
    {
        var js = await ConnectedAsync();
        var denied = true;
        js.Respond = command =>
        {
            if (command[0] == 0x02 && denied)
            {
                denied = false;
                return [0x80, 0x02, 0x05];
            }
            return [0x80, command[0], 0x01];
        };

        var result = await js.Treadmill.SetSpeedAsync(5);

        Assert.AreEqual(ControlPointResult.Success, result);
        Assert.HasCount(3, js.Writes);
        CollectionAssert.AreEqual(Hex("02 F4 01"), js.Writes[0]);
        CollectionAssert.AreEqual(Hex("00"), js.Writes[1]);
        CollectionAssert.AreEqual(Hex("02 F4 01"), js.Writes[2]);
    }

    [TestMethod]
    public async Task Control_not_permitted_twice_is_returned_without_looping()
    {
        var js = await ConnectedAsync();
        js.Respond = command => command[0] == 0x00 ? [0x80, 0x00, 0x01] : [0x80, command[0], 0x05];

        var result = await js.Treadmill.StartAsync();

        Assert.AreEqual(ControlPointResult.ControlNotPermitted, result);
        Assert.HasCount(3, js.Writes);
    }

    [TestMethod]
    public async Task Rejection_is_returned_to_the_caller()
    {
        var js = await ConnectedAsync();
        js.Respond = command => [0x80, command[0], 0x03];

        Assert.AreEqual(ControlPointResult.InvalidParameter, await js.Treadmill.SetInclineAsync(5));
    }

    [TestMethod]
    public async Task Missing_answer_times_out()
    {
        var js = await ConnectedAsync();
        js.Respond = _ => null;

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => js.Treadmill.StartAsync());

        // The queue is released: the next command goes through.
        js.Respond = command => [0x80, command[0], 0x01];
        Assert.AreEqual(ControlPointResult.Success, await js.Treadmill.StartAsync());
    }

    [TestMethod]
    public async Task Answer_to_another_request_is_ignored()
    {
        var js = await ConnectedAsync();
        js.Respond = _ => [0x80, 0x07, 0x01]; // answers "start" whatever the request

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => js.Treadmill.SetSpeedAsync(5));
    }

    [TestMethod]
    public async Task Unexpected_disconnection_is_reported_and_blocks_commands()
    {
        var js = await ConnectedAsync();
        var changes = 0;
        js.Treadmill.StateChanged += () => changes++;

        js.Treadmill.OnDisconnected();

        Assert.AreEqual(TreadmillState.ConnectionLost, js.Treadmill.State);
        Assert.AreEqual(1, changes);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => js.Treadmill.SetSpeedAsync(5));
        Assert.IsEmpty(js.Writes);
    }

    [TestMethod]
    public async Task User_disconnection_is_not_a_connection_loss()
    {
        var js = await ConnectedAsync();

        await js.Treadmill.DisconnectAsync();
        js.Treadmill.OnDisconnected(); // browser event that follows

        Assert.AreEqual(TreadmillState.Disconnected, js.Treadmill.State);
    }

    [TestMethod]
    public async Task Notifications_are_decoded_and_raised()
    {
        var js = await ConnectedAsync();
        TreadmillData? received = null;
        MachineStatus? status = null;
        js.Treadmill.DataReceived += d => received = d;
        js.Treadmill.StatusReceived += s => status = s;

        js.Treadmill.OnTreadmillData(Hex("00 00 F4 01"));
        js.Treadmill.OnTreadmillData(Hex("8C 04")); // truncated: ignored
        js.Treadmill.OnMachineStatus(Hex("03"));

        Assert.AreEqual(5.0, received!.SpeedKmh!.Value, 1e-9);
        Assert.AreSame(received, js.Treadmill.LastData);
        Assert.AreEqual(MachineStatus.StoppedBySafetyKey, status);
    }
}
