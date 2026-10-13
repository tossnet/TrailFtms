using System.Buffers.Binary;
using Microsoft.Extensions.Time.Testing;
using TrailFtms.Services.Bluetooth;
using TrailFtms.Services.Programs;
using TrailFtms.Services.Ride;
using TrailFtms.Services.Video;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Services.Ride;

[TestClass]
public sealed class RideEngineTests
{
    // Treadmill ranges from FakeFtmsJs: 1.0 to 18.0 km/h step 0.1, 0 to 15 % step 1.
    private static readonly RideProgram Program = new()
    {
        Id = "test",
        Title = "Test",
        YoutubeId = "KiEN4HxM4ic",
        DurationSeconds = 600,
        Segments = [new(0, 1.0, 0), new(40, 3.0, 3), new(50, 3.0, 1), new(60, 3.2, 0)],
    };

    /// <summary>Connected treadmill on a fake JS module, fake video, engine with a frozen clock (ticks are manual).</summary>
    private sealed class Rig
    {
        public FakeFtmsJs Js { get; } = new();
        public FakeVideoPlayer Video { get; } = new();
        public RideEngine Engine { get; private set; } = null!;
        public List<RideWarning> Warnings { get; } = [];

        public static async Task<Rig> StartAsync(RideSettings? settings = null)
        {
            var rig = new Rig();
            await rig.Js.Treadmill.ConnectAsync();
            await rig.Video.LoadAsync(default, Program.YoutubeId);
            rig.Engine = new RideEngine(rig.Js.Treadmill, rig.Video, new FakeTimeProvider()) { Settings = settings ?? new() };
            rig.Engine.WarningRaised += rig.Warnings.Add;
            rig.Engine.Start(Program);
            rig.Js.Writes.Clear();
            return rig;
        }

        /// <summary>Readable list of the frames written to the Control Point since the last call.</summary>
        public List<string> TakeSent()
        {
            var sent = Js.Writes.Select(Describe).ToList();
            Js.Writes.Clear();
            return sent;
        }

        public async Task PlayAtAsync(double seconds)
        {
            Video.CurrentTime = seconds;
            if (Video.State != VideoState.Playing)
                Video.SetState(VideoState.Playing);
            await Engine.TickAsync();
        }

        public async Task TicksAsync(int count)
        {
            for (var i = 0; i < count; i++)
                await Engine.TickAsync();
        }

        private static string Describe(byte[] frame) => frame[0] switch
        {
            0x00 => "control",
            0x07 => "start",
            0x08 => frame[1] == 0x01 ? "stop" : "pause",
            0x02 => FormattableString.Invariant($"speed {BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) / 100.0}"),
            0x03 => FormattableString.Invariant($"incline {BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(1)) / 10.0}"),
            _ => Convert.ToHexString(frame),
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20);
    }

    // ---- Following the video ----

    [TestMethod]
    public async Task Nothing_is_sent_before_the_video_plays()
    {
        var rig = await Rig.StartAsync();

        await rig.TicksAsync(5);

        Assert.IsEmpty(rig.TakeSent());
        Assert.AreEqual(RideStatus.Running, rig.Engine.Status);
    }

    [TestMethod]
    public async Task Play_starts_the_belt_and_applies_the_first_segment()
    {
        var rig = await Rig.StartAsync();

        await rig.PlayAtAsync(0);

        CollectionAssert.AreEqual(new[] { "start", "speed 1", "incline 0" }, rig.TakeSent());
        Assert.AreEqual(0, rig.Engine.SegmentIndex);
        Assert.AreEqual(40, rig.Engine.NextChangeIn);
    }

    [TestMethod]
    public async Task Commands_are_sent_only_when_targets_change()
    {
        var rig = await Rig.StartAsync();
        await rig.PlayAtAsync(0);
        rig.TakeSent();

        await rig.PlayAtAsync(10);
        await rig.PlayAtAsync(20);
        Assert.IsEmpty(rig.TakeSent());

        await rig.PlayAtAsync(45);
        CollectionAssert.AreEqual(new[] { "speed 3", "incline 3" }, rig.TakeSent());

        await rig.PlayAtAsync(55); // same speed, lower incline
        CollectionAssert.AreEqual(new[] { "incline 1" }, rig.TakeSent());
    }

    [TestMethod]
    public async Task Seek_back_reapplies_the_matching_segment()
    {
        var rig = await Rig.StartAsync();
        await rig.PlayAtAsync(65);
        rig.TakeSent();

        await rig.PlayAtAsync(12);

        CollectionAssert.AreEqual(new[] { "speed 1" }, rig.TakeSent()); // incline already 0
        Assert.AreEqual(0, rig.Engine.SegmentIndex);
    }

    [TestMethod]
    public async Task Buffering_keeps_the_current_targets()
    {
        var rig = await Rig.StartAsync();
        await rig.PlayAtAsync(45);
        rig.TakeSent();

        rig.Video.SetState(VideoState.Buffering);
        await rig.TicksAsync(4);

        Assert.IsEmpty(rig.TakeSent());
    }

    [TestMethod]
    public async Task Last_segment_has_no_next_change()
    {
        var rig = await Rig.StartAsync();

        await rig.PlayAtAsync(300);

        Assert.AreEqual(3, rig.Engine.SegmentIndex);
        Assert.IsNull(rig.Engine.NextChangeIn);
    }

    // ---- Settings ----

    [TestMethod]
    public async Task Speed_factor_is_applied_and_snapped_to_machine_step()
    {
        var rig = await Rig.StartAsync(new RideSettings { SpeedFactor = 1.1 });

        await rig.PlayAtAsync(65); // 3.2 x 1.1 = 3.52

        CollectionAssert.Contains(rig.TakeSent(), "speed 3.5");
        Assert.AreEqual(3.5, rig.Engine.TargetSpeed);
    }

    [TestMethod]
    public async Task Changing_the_factor_during_the_ride_is_applied_at_next_tick()
    {
        var rig = await Rig.StartAsync();
        await rig.PlayAtAsync(45);
        rig.TakeSent();

        rig.Engine.Settings = rig.Engine.Settings with { SpeedFactor = 0.9 };
        await rig.Engine.TickAsync();

        CollectionAssert.AreEqual(new[] { "speed 2.7" }, rig.TakeSent());
    }

    [TestMethod]
    public async Task User_ceilings_are_applied_before_sending()
    {
        var rig = await Rig.StartAsync();
        rig.Js.Treadmill.Limits = new TreadmillLimits(MaxSpeed: 2.5, MaxIncline: 2);

        await rig.PlayAtAsync(45); // program: 3 km/h, 3 %

        CollectionAssert.AreEqual(new[] { "start", "speed 2.5", "incline 2" }, rig.TakeSent());
    }

    // ---- Pause ----

    [TestMethod]
    public async Task Pause_slows_down_then_resume_restores_the_program()
    {
        var rig = await Rig.StartAsync(new RideSettings { PauseBehavior = PauseBehavior.SlowDown, PauseSpeed = 2.0 });
        await rig.PlayAtAsync(65);
        rig.TakeSent();

        rig.Video.SetState(VideoState.Paused);
        await rig.TicksAsync(4);
        CollectionAssert.AreEqual(new[] { "speed 2" }, rig.TakeSent());

        await rig.PlayAtAsync(66);
        CollectionAssert.AreEqual(new[] { "speed 3.2" }, rig.TakeSent());
    }

    [TestMethod]
    public async Task Pause_never_speeds_up_the_belt()
    {
        var rig = await Rig.StartAsync(new RideSettings { PauseBehavior = PauseBehavior.SlowDown, PauseSpeed = 3.0 });
        await rig.PlayAtAsync(0); // 1 km/h
        rig.TakeSent();

        rig.Video.SetState(VideoState.Paused);

        Assert.IsEmpty(rig.TakeSent());
    }

    [TestMethod]
    public async Task Pause_can_pause_the_treadmill_and_restart_it_on_resume()
    {
        var rig = await Rig.StartAsync(new RideSettings { PauseBehavior = PauseBehavior.PauseTreadmill });
        await rig.PlayAtAsync(45);
        rig.TakeSent();

        rig.Video.SetState(VideoState.Paused);
        rig.Js.Treadmill.OnMachineStatus([0x02, 0x02]); // the machine reports our own pause
        CollectionAssert.AreEqual(new[] { "pause" }, rig.TakeSent());
        Assert.AreEqual(RideStatus.Running, rig.Engine.Status);

        await rig.PlayAtAsync(46);
        CollectionAssert.AreEqual(new[] { "start" }, rig.TakeSent());
    }

    [TestMethod]
    public async Task Pause_can_be_ignored()
    {
        var rig = await Rig.StartAsync(new RideSettings { PauseBehavior = PauseBehavior.Nothing });
        await rig.PlayAtAsync(45);
        rig.TakeSent();

        rig.Video.SetState(VideoState.Paused);
        await rig.TicksAsync(4);

        Assert.IsEmpty(rig.TakeSent());
    }

    [TestMethod]
    public async Task Pause_before_the_first_play_sends_nothing()
    {
        var rig = await Rig.StartAsync(new RideSettings { PauseBehavior = PauseBehavior.PauseTreadmill });

        rig.Video.SetState(VideoState.Paused);

        Assert.IsEmpty(rig.TakeSent());
    }

    // ---- End of video ----

    [TestMethod]
    public async Task End_of_video_cools_down_progressively_then_stops()
    {
        var rig = await Rig.StartAsync();
        await rig.PlayAtAsync(45); // 3 km/h, 3 %
        rig.TakeSent();

        rig.Video.SetState(VideoState.Ended); // first step right away
        Assert.AreEqual(RideStatus.CoolingDown, rig.Engine.Status);
        CollectionAssert.AreEqual(new[] { "incline 2" }, rig.TakeSent());

        await rig.TicksAsync(RideEngine.CooldownStepTicks - 1);
        Assert.IsEmpty(rig.TakeSent(), "one step every CooldownStepTicks");

        var steps = new List<string>();
        for (var i = 0; i < 10 && rig.Engine.Status == RideStatus.CoolingDown; i++)
        {
            await rig.Engine.TickAsync();
            steps.AddRange(rig.TakeSent());
            await rig.TicksAsync(RideEngine.CooldownStepTicks - 1);
        }

        CollectionAssert.AreEqual(
            new[] { "incline 1", "incline 0", "speed 2.5", "speed 2", "speed 1.5", "speed 1", "stop" },
            steps);
        Assert.AreEqual(RideStatus.Finished, rig.Engine.Status);
    }

    // ---- Safety ----

    [TestMethod]
    public async Task Connection_loss_halts_and_sends_nothing_more()
    {
        var rig = await Rig.StartAsync();
        await rig.PlayAtAsync(0);
        rig.TakeSent();

        rig.Js.Treadmill.OnDisconnected();
        await rig.PlayAtAsync(45);

        Assert.AreEqual(RideStatus.Halted, rig.Engine.Status);
        Assert.AreEqual(HaltReason.ConnectionLost, rig.Engine.HaltReason);
        Assert.IsEmpty(rig.TakeSent());
    }

    [TestMethod]
    public async Task Safety_key_halts_and_pauses_the_video()
    {
        var rig = await Rig.StartAsync();
        await rig.PlayAtAsync(0);
        rig.TakeSent();

        rig.Js.Treadmill.OnMachineStatus([0x03]);
        await rig.TicksAsync(4);

        Assert.AreEqual(HaltReason.StoppedFromTreadmill, rig.Engine.HaltReason);
        Assert.AreEqual(VideoState.Paused, rig.Video.State);
        Assert.IsEmpty(rig.TakeSent());
    }

    [TestMethod]
    public async Task Stop_from_the_console_halts()
    {
        var rig = await Rig.StartAsync();
        await rig.PlayAtAsync(0);

        rig.Js.Treadmill.OnMachineStatus([0x02, 0x01]);

        Assert.AreEqual(HaltReason.StoppedFromTreadmill, rig.Engine.HaltReason);
    }

    [TestMethod]
    public async Task User_stop_stops_belt_and_video_then_nothing_is_sent()
    {
        var rig = await Rig.StartAsync();
        await rig.PlayAtAsync(45);
        rig.TakeSent();

        await rig.Engine.StopAsync();
        await rig.PlayAtAsync(65);

        CollectionAssert.AreEqual(new[] { "stop" }, rig.TakeSent());
        Assert.AreEqual(HaltReason.StoppedByUser, rig.Engine.HaltReason);
    }

    [TestMethod]
    public async Task Restart_after_a_halt_starts_the_belt_again()
    {
        var rig = await Rig.StartAsync();
        await rig.PlayAtAsync(45);
        await rig.Engine.StopAsync();
        rig.TakeSent();

        rig.Engine.Start(Program);
        await rig.PlayAtAsync(46);

        CollectionAssert.AreEqual(new[] { "start", "speed 3", "incline 3" }, rig.TakeSent());
    }

    [TestMethod]
    public async Task Rejected_command_warns_once_and_is_not_retried_every_tick()
    {
        var rig = await Rig.StartAsync();
        rig.Js.Respond = command => command[0] == 0x03 ? [0x80, 0x03, 0x03] : [0x80, command[0], 0x01];

        await rig.PlayAtAsync(45);
        await rig.TicksAsync(4);

        CollectionAssert.AreEqual(new[] { "start", "speed 3", "incline 3" }, rig.TakeSent());
        CollectionAssert.AreEqual(new[] { RideWarning.CommandRejected }, rig.Warnings);
    }

    [TestMethod]
    public async Task Missing_answer_warns_and_the_ride_goes_on()
    {
        var rig = await Rig.StartAsync();
        rig.Js.Respond = command => command[0] == 0x02 ? null : [0x80, command[0], 0x01];

        await rig.PlayAtAsync(45);
        await WaitUntilAsync(() => rig.Warnings.Count > 0); // the tick started by "play" waits for the timeout

        CollectionAssert.AreEqual(new[] { RideWarning.NoResponse }, rig.Warnings);
        Assert.AreEqual(RideStatus.Running, rig.Engine.Status);
        CollectionAssert.Contains(rig.TakeSent(), "incline 3");
    }
}
