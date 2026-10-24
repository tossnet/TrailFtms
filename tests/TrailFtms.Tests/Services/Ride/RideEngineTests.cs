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

    /// <summary>Incline sent exactly at the segment time, so the tests below read like the program.</summary>
    private static readonly RideSettings NoLead = new() { InclineSecondsPerStep = 0 };

    /// <summary>Connected treadmill on a fake JS module, fake video, engine with a frozen clock (ticks are manual).</summary>
    private sealed class Rig
    {
        public FakeFtmsJs Js { get; } = new();
        public FakeVideoPlayer Video { get; } = new();
        public RideEngine Engine { get; private set; } = null!;
        public List<RideWarning> Warnings { get; } = [];

        public static async Task<Rig> StartAsync(RideSettings? settings = null, bool bike = false)
        {
            var rig = new Rig();
            if (bike)
                rig.Js.Protocol = "domyos";
            await rig.Js.Treadmill.ConnectAsync();
            await rig.Video.LoadAsync(default, Program.YoutubeId);
            rig.Engine = new RideEngine(rig.Js.Treadmill, rig.Video, new FakeTimeProvider()) { Settings = settings ?? NoLead };
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

        /// <summary>Treadmill Data notification with the measured speed and incline.</summary>
        public void Measure(double speed, double incline)
        {
            var frame = new byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(frame, 0x0008); // speed (bit 0 clear) + inclination
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), (ushort)Math.Round(speed * 100));
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), (short)Math.Round(incline * 10));
            Js.Treadmill.OnTreadmillData(frame);
        }

        /// <summary>Domyos measurement frame with the pedalled speed and the resistance level, in its two notifications.</summary>
        public void MeasureBike(double speed, int level)
        {
            var parameters = new byte[23];
            parameters.AsSpan().Fill(0xFF);
            BinaryPrimitives.WriteUInt16BigEndian(parameters.AsSpan(4), (ushort)Math.Round(speed * 10));
            parameters[12] = (byte)level;
            var frame = DomyosProtocol.Frame(0xBC, parameters);
            Js.Treadmill.OnDomyosData(frame[..20]);
            Js.Treadmill.OnDomyosData(frame[20..]);
        }

        /// <summary>Plays from <paramref name="from"/> to <paramref name="to"/> (one tick per second) with these measures.</summary>
        public async Task RideAsync(double from, double to, double speed, double incline)
        {
            Measure(speed, incline);
            for (var t = from; t <= to; t++)
                await PlayAtAsync(t);
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
            0xF0 when frame[1] == 0xAD => $"level {frame[10]}",
            _ => Convert.ToHexString(frame),
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        // Generous bound: only reached when the test fails, CI machines can be slow.
        for (var i = 0; i < 250 && !condition(); i++)
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
    public async Task Slow_video_start_does_not_start_the_belt_before_the_video_plays()
    {
        var rig = await Rig.StartAsync();

        rig.Video.SetState(VideoState.Buffering); // play pressed, video still loading
        await rig.TicksAsync(20);
        Assert.IsEmpty(rig.TakeSent());

        await rig.PlayAtAsync(0);
        CollectionAssert.AreEqual(new[] { "start", "speed 1", "incline 0" }, rig.TakeSent());
    }

    [TestMethod]
    public async Task Seek_during_the_ride_reapplies_the_segment_while_buffering()
    {
        var rig = await Rig.StartAsync();
        await rig.PlayAtAsync(65);
        rig.TakeSent();

        rig.Video.CurrentTime = 45; // seek back: YouTube buffers before playing again
        rig.Video.SetState(VideoState.Buffering);

        CollectionAssert.AreEqual(new[] { "speed 3", "incline 3" }, rig.TakeSent());
    }

    [TestMethod]
    public async Task After_a_treadmill_pause_the_belt_restarts_only_when_the_video_plays()
    {
        var rig = await Rig.StartAsync(NoLead with {PauseBehavior = PauseBehavior.PauseTreadmill });
        await rig.PlayAtAsync(45);
        rig.Video.SetState(VideoState.Paused);
        rig.TakeSent();

        rig.Video.SetState(VideoState.Buffering); // resume pressed, video still loading
        await rig.TicksAsync(20);
        Assert.IsEmpty(rig.TakeSent());

        await rig.PlayAtAsync(46);
        CollectionAssert.AreEqual(new[] { "start" }, rig.TakeSent());
    }

    [TestMethod]
    public async Task After_a_slow_down_the_speed_comes_back_only_when_the_video_plays()
    {
        var rig = await Rig.StartAsync(NoLead with {PauseBehavior = PauseBehavior.SlowDown, PauseSpeed = 2.0 });
        await rig.PlayAtAsync(65);
        rig.Video.SetState(VideoState.Paused);
        rig.TakeSent();

        rig.Video.SetState(VideoState.Buffering);
        await rig.TicksAsync(20);
        Assert.IsEmpty(rig.TakeSent());

        await rig.PlayAtAsync(66);
        CollectionAssert.AreEqual(new[] { "speed 3.2" }, rig.TakeSent());
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
        var rig = await Rig.StartAsync(NoLead with {SpeedFactor = 1.1 });

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

    // ---- Incline anticipation ----

    [TestMethod]
    public async Task Incline_change_is_sent_early_by_the_travel_time()
    {
        var rig = await Rig.StartAsync(new RideSettings { InclineSecondsPerStep = 2.5 });
        await rig.PlayAtAsync(0);
        rig.TakeSent();

        await rig.PlayAtAsync(32); // 0 -> 3 % at 40 s: 3 steps x 2.5 s = 7.5 s ahead
        Assert.IsEmpty(rig.TakeSent());

        await rig.PlayAtAsync(32.5);
        CollectionAssert.AreEqual(new[] { "incline 3" }, rig.TakeSent());
        Assert.AreEqual(0, rig.Engine.SegmentIndex, "speed and segment still follow the video time");
        Assert.AreEqual(1.0, rig.Engine.TargetSpeed);

        await rig.PlayAtAsync(40);
        CollectionAssert.AreEqual(new[] { "speed 3" }, rig.TakeSent());
    }

    [TestMethod]
    public async Task Small_incline_change_is_anticipated_less_than_a_big_one()
    {
        var rig = await Rig.StartAsync(new RideSettings { InclineSecondsPerStep = 2.5 });
        await rig.PlayAtAsync(44);
        rig.TakeSent();

        await rig.PlayAtAsync(45); // 3 -> 1 % at 50 s: 2 steps = 5 s ahead
        CollectionAssert.AreEqual(new[] { "incline 1" }, rig.TakeSent());

        await rig.PlayAtAsync(57); // 1 -> 0 % at 60 s: 1 step = 2.5 s ahead
        Assert.IsEmpty(rig.TakeSent());
        await rig.PlayAtAsync(57.5);
        CollectionAssert.AreEqual(new[] { "incline 0" }, rig.TakeSent());
    }

    [TestMethod]
    public async Task Speed_only_changes_do_not_hide_the_next_incline_change()
    {
        var program = Program with
        {
            Segments = [new(0, 1.0, 0), new(10, 2.0, 0), new(12, 2.5, 0), new(20, 2.5, 2)],
        };
        var rig = await Rig.StartAsync(new RideSettings { InclineSecondsPerStep = 2.5 });
        rig.Engine.Start(program);

        await rig.PlayAtAsync(11); // 2 steps = 5 s before 20 s, two speed changes in between
        Assert.AreEqual(0, rig.Engine.TargetIncline);

        await rig.PlayAtAsync(15);
        Assert.AreEqual(2, rig.Engine.TargetIncline);
    }

    [TestMethod]
    public async Task Anticipation_uses_the_clamped_incline()
    {
        var rig = await Rig.StartAsync(new RideSettings { InclineSecondsPerStep = 2.5 });
        rig.Js.Treadmill.Limits = new TreadmillLimits(MaxSpeed: null, MaxIncline: 1);
        await rig.PlayAtAsync(0);
        rig.TakeSent();

        await rig.PlayAtAsync(37); // capped to 1 %: only 1 step, 2.5 s ahead
        Assert.IsEmpty(rig.TakeSent());
        await rig.PlayAtAsync(37.5);
        CollectionAssert.AreEqual(new[] { "incline 1" }, rig.TakeSent());
    }

    // ---- Pause ----

    [TestMethod]
    public async Task Pause_slows_down_then_resume_restores_the_program()
    {
        var rig = await Rig.StartAsync(NoLead with {PauseBehavior = PauseBehavior.SlowDown, PauseSpeed = 2.0 });
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
        var rig = await Rig.StartAsync(NoLead with {PauseBehavior = PauseBehavior.SlowDown, PauseSpeed = 3.0 });
        await rig.PlayAtAsync(0); // 1 km/h
        rig.TakeSent();

        rig.Video.SetState(VideoState.Paused);

        Assert.IsEmpty(rig.TakeSent());
    }

    [TestMethod]
    public async Task Pause_can_pause_the_treadmill_and_restart_it_on_resume()
    {
        var rig = await Rig.StartAsync(NoLead with {PauseBehavior = PauseBehavior.PauseTreadmill });
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
        var rig = await Rig.StartAsync(NoLead with {PauseBehavior = PauseBehavior.Nothing });
        await rig.PlayAtAsync(45);
        rig.TakeSent();

        rig.Video.SetState(VideoState.Paused);
        await rig.TicksAsync(4);

        Assert.IsEmpty(rig.TakeSent());
    }

    [TestMethod]
    public async Task Pause_before_the_first_play_sends_nothing()
    {
        var rig = await Rig.StartAsync(NoLead with {PauseBehavior = PauseBehavior.PauseTreadmill });

        rig.Video.SetState(VideoState.Paused);

        Assert.IsEmpty(rig.TakeSent());
    }

    // ---- Ads ----

    [TestMethod]
    public async Task Ad_keeps_the_current_pace_then_the_ride_resumes()
    {
        var rig = await Rig.StartAsync();
        rig.Video.Duration = 600;
        await rig.PlayAtAsync(45); // 3 km/h, 3 %
        rig.TakeSent();

        rig.Video.Duration = 30; // mid-roll: time and duration are the ad's
        await rig.PlayAtAsync(2);
        await rig.PlayAtAsync(3);

        Assert.IsTrue(rig.Engine.IsAdPlaying);
        Assert.IsEmpty(rig.TakeSent());
        Assert.AreEqual(45, rig.Engine.VideoTime);

        rig.Video.Duration = 600;
        await rig.PlayAtAsync(65);

        Assert.IsFalse(rig.Engine.IsAdPlaying);
        CollectionAssert.AreEqual(new[] { "speed 3.2", "incline 0" }, rig.TakeSent());
    }

    [TestMethod]
    public async Task Ad_ending_does_not_end_the_ride()
    {
        var rig = await Rig.StartAsync();
        rig.Video.Duration = 600;
        await rig.PlayAtAsync(45);
        rig.TakeSent();

        rig.Video.Duration = 30;
        await rig.PlayAtAsync(30);
        rig.Video.SetState(VideoState.Ended);

        Assert.AreEqual(RideStatus.Running, rig.Engine.Status);
        Assert.IsEmpty(rig.TakeSent());
    }

    [TestMethod]
    public async Task Ad_is_detected_against_the_program_duration_before_the_video_plays()
    {
        var rig = await Rig.StartAsync();
        await rig.PlayAtAsync(45);
        rig.TakeSent();

        rig.Video.Duration = 15; // program lasts 600 s
        await rig.PlayAtAsync(1);

        Assert.IsTrue(rig.Engine.IsAdPlaying);
        Assert.IsEmpty(rig.TakeSent());
    }

    // ---- Bike ----

    [TestMethod]
    public async Task Bike_follows_the_incline_with_the_resistance_at_once()
    {
        var rig = await Rig.StartAsync(new RideSettings(), bike: true); // incline lead of the treadmill motor

        await rig.PlayAtAsync(0);
        CollectionAssert.AreEqual(new[] { "level 1" }, rig.TakeSent());

        await rig.PlayAtAsync(39);
        Assert.IsEmpty(rig.TakeSent());

        await rig.PlayAtAsync(40);
        CollectionAssert.AreEqual(new[] { "level 4" }, rig.TakeSent());
        Assert.AreEqual(3.0, rig.Engine.TargetSpeed, "program speed still shown");
    }

    [TestMethod]
    public async Task Bike_cools_down_the_resistance_only()
    {
        var rig = await Rig.StartAsync(bike: true);
        await rig.PlayAtAsync(45); // 3 %: level 4
        rig.TakeSent();

        rig.Video.SetState(VideoState.Ended);
        var steps = rig.TakeSent();
        for (var i = 0; i < 10 && rig.Engine.Status == RideStatus.CoolingDown; i++)
        {
            await rig.TicksAsync(RideEngine.CooldownStepTicks);
            steps.AddRange(rig.TakeSent());
        }

        CollectionAssert.AreEqual(new[] { "level 3", "level 2", "level 1", "level 1" }, steps); // the last one is the stop
        Assert.AreEqual(RideStatus.Finished, rig.Engine.Status);
    }

    [TestMethod]
    public async Task Bike_records_resistance_changes_but_not_the_pedalled_speed()
    {
        var rig = await Rig.StartAsync(bike: true);
        rig.MeasureBike(25, 4);
        for (var t = 40; t <= 45; t++)
            await rig.PlayAtAsync(t); // 3 % from 40: level 4 reached

        rig.MeasureBike(32, 6);
        for (var t = 46; t <= 49; t++)
            await rig.PlayAtAsync(t);

        var adjustment = rig.Engine.Adjustments.Single();
        Assert.AreEqual(AdjustedValue.Incline, adjustment.Kind);
        Assert.AreEqual(5.0, adjustment.Value);
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
    public async Task Disconnect_button_halts_as_a_user_stop()
    {
        var rig = await Rig.StartAsync();
        await rig.PlayAtAsync(45);

        await rig.Js.Treadmill.DisconnectAsync();

        Assert.AreEqual(RideStatus.Halted, rig.Engine.Status);
        Assert.AreEqual(HaltReason.StoppedByUser, rig.Engine.HaltReason);
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

    // ---- Changes made on the treadmill console ----

    [TestMethod]
    public async Task A_change_kept_on_the_console_is_recorded_from_its_start()
    {
        var rig = await Rig.StartAsync();
        await rig.RideAsync(0, 9, 1.0, 0);

        await rig.RideAsync(10, 11, 1.2, 0); // pressing "+" several times
        await rig.RideAsync(12, 20, 1.5, 0);

        CollectionAssert.AreEqual(new[] { new RideAdjustment(10, AdjustedValue.Speed, 1.5) }, rig.Engine.Adjustments.ToArray());
    }

    [TestMethod]
    public async Task A_short_change_is_ignored()
    {
        var rig = await Rig.StartAsync();
        await rig.RideAsync(0, 9, 1.0, 0);

        await rig.RideAsync(10, 11, 1.5, 0);
        await rig.RideAsync(12, 20, 1.0, 0);

        Assert.IsEmpty(rig.Engine.Adjustments);
    }

    [TestMethod]
    public async Task The_treadmill_moving_to_a_new_target_is_not_a_change()
    {
        var rig = await Rig.StartAsync();
        await rig.RideAsync(0, 39, 1.0, 0);

        await rig.RideAsync(40, 44, 2.0, 1); // belt and incline motor still on their way to 3 km/h and 3 %
        await rig.RideAsync(45, 49, 3.0, 3);

        Assert.IsEmpty(rig.Engine.Adjustments);
    }

    [TestMethod]
    public async Task An_incline_change_is_recorded()
    {
        var rig = await Rig.StartAsync();
        await rig.RideAsync(0, 9, 1.0, 0);
        rig.TakeSent();

        await rig.RideAsync(10, 20, 1.0, 2);

        CollectionAssert.AreEqual(new[] { new RideAdjustment(10, AdjustedValue.Incline, 2) }, rig.Engine.Adjustments.ToArray());
        Assert.IsEmpty(rig.TakeSent()); // the user's value is not fought
    }

    [TestMethod]
    public async Task A_speed_change_is_recorded_in_program_units()
    {
        var rig = await Rig.StartAsync(NoLead with { SpeedFactor = 1.1 });
        await rig.RideAsync(0, 9, 1.1, 0);

        await rig.RideAsync(10, 20, 2.2, 0);

        Assert.AreEqual(new RideAdjustment(10, AdjustedValue.Speed, 2.0), rig.Engine.Adjustments.Single());
    }

    [TestMethod]
    public async Task Changes_made_while_the_video_is_paused_are_ignored()
    {
        var rig = await Rig.StartAsync(NoLead with { PauseBehavior = PauseBehavior.Nothing });
        await rig.RideAsync(0, 9, 1.0, 0);

        rig.Video.SetState(VideoState.Paused);
        await rig.TicksAsync(1);
        rig.Measure(2.0, 0);
        await rig.TicksAsync(20);
        await rig.RideAsync(10, 20, 2.0, 0);

        Assert.IsEmpty(rig.Engine.Adjustments);
    }
}
