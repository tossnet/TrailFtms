using TrailFtms.Services.Bluetooth;

namespace TrailFtms.Tests.Services.Bluetooth;

[TestClass]
public sealed class FtmsProtocolTests
{
    private const double Tolerance = 1e-9;

    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    // ---- Commands ----

    [TestMethod]
    public void Simple_commands_are_single_op_code()
    {
        CollectionAssert.AreEqual(Hex("00"), FtmsProtocol.RequestControl());
        CollectionAssert.AreEqual(Hex("01"), FtmsProtocol.Reset());
        CollectionAssert.AreEqual(Hex("07"), FtmsProtocol.StartOrResume());
    }

    [TestMethod]
    public void StopOrPause_carries_its_parameter()
    {
        CollectionAssert.AreEqual(Hex("08 01"), FtmsProtocol.StopOrPause(StopKind.Stop));
        CollectionAssert.AreEqual(Hex("08 02"), FtmsProtocol.StopOrPause(StopKind.Pause));
    }

    [TestMethod]
    [DataRow(5.0, "02 F4 01")]   // validated on CARE Run-18
    [DataRow(1.0, "02 64 00")]
    [DataRow(18.0, "02 08 07")]
    [DataRow(5.004, "02 F4 01")] // rounded to 0.01 km/h
    [DataRow(0.0, "02 00 00")]
    public void SetTargetSpeed_encodes_uint16_in_hundredths(double kmh, string expected)
    {
        CollectionAssert.AreEqual(Hex(expected), FtmsProtocol.SetTargetSpeed(kmh));
    }

    [TestMethod]
    [DataRow(2.0, "03 14 00")]   // validated on CARE Run-18
    [DataRow(15.0, "03 96 00")]
    [DataRow(-1.5, "03 F1 FF")]
    [DataRow(0.0, "03 00 00")]
    public void SetTargetInclination_encodes_sint16_in_tenths(double percent, string expected)
    {
        CollectionAssert.AreEqual(Hex(expected), FtmsProtocol.SetTargetInclination(percent));
    }

    [TestMethod]
    public void Out_of_range_targets_throw()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FtmsProtocol.SetTargetSpeed(-0.1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FtmsProtocol.SetTargetSpeed(700));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FtmsProtocol.SetTargetInclination(4000));
    }

    // ---- Control Point response ----

    [TestMethod]
    public void ParseControlPointResponse_decodes_request_and_result()
    {
        var success = FtmsProtocol.ParseControlPointResponse(Hex("80 02 01"));
        Assert.AreEqual(new ControlPointResponse(ControlPointOpCode.SetTargetSpeed, ControlPointResult.Success), success);
        Assert.IsTrue(success!.Value.IsSuccess);

        var denied = FtmsProtocol.ParseControlPointResponse(Hex("80 03 05"));
        Assert.AreEqual(ControlPointOpCode.SetTargetInclination, denied!.Value.RequestOpCode);
        Assert.AreEqual(ControlPointResult.ControlNotPermitted, denied.Value.Result);
        Assert.IsFalse(denied.Value.IsSuccess);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("80 02")]
    [DataRow("02 F4 01")]
    public void ParseControlPointResponse_rejects_other_frames(string hex)
    {
        Assert.IsNull(FtmsProtocol.ParseControlPointResponse(Hex(hex)));
    }

    // ---- Machine status ----

    [TestMethod]
    [DataRow("03", MachineStatus.StoppedBySafetyKey)]
    [DataRow("02 01", MachineStatus.StoppedOrPausedByUser)]
    [DataRow("05 F4 01", MachineStatus.TargetSpeedChanged)]
    [DataRow("FF", MachineStatus.ControlPermissionLost)]
    public void ParseMachineStatus_reads_op_code(string hex, MachineStatus expected)
    {
        Assert.AreEqual(expected, FtmsProtocol.ParseMachineStatus(Hex(hex)));
    }

    [TestMethod]
    public void ParseMachineStatus_rejects_empty_frame()
    {
        Assert.IsNull(FtmsProtocol.ParseMachineStatus([]));
    }

    // ---- Capabilities ----

    [TestMethod]
    public void ParseMachineFeatures_decodes_both_bit_fields()
    {
        // Machine: total distance, inclination, expended energy, elapsed time. Target: speed, inclination.
        var features = FtmsProtocol.ParseMachineFeatures(Hex("0C 12 00 00 03 00 00 00"));

        Assert.IsNotNull(features);
        Assert.AreEqual(
            FitnessMachineFeatures.TotalDistance | FitnessMachineFeatures.Inclination
            | FitnessMachineFeatures.ExpendedEnergy | FitnessMachineFeatures.ElapsedTime,
            features.Value.Machine);
        Assert.IsTrue(features.Value.CanSetSpeed);
        Assert.IsTrue(features.Value.CanSetInclination);
        Assert.IsNull(FtmsProtocol.ParseMachineFeatures(Hex("0C 12 00 00")));
    }

    [TestMethod]
    public void ParseSpeedRange_decodes_run18_range()
    {
        var range = FtmsProtocol.ParseSpeedRange(Hex("64 00 08 07 0A 00"));

        Assert.IsNotNull(range);
        Assert.AreEqual(1.0, range.Value.Min, Tolerance);
        Assert.AreEqual(18.0, range.Value.Max, Tolerance);
        Assert.AreEqual(0.1, range.Value.Step, Tolerance);
    }

    [TestMethod]
    public void ParseInclinationRange_decodes_signed_bounds()
    {
        var run18 = FtmsProtocol.ParseInclinationRange(Hex("00 00 96 00 0A 00"));
        Assert.AreEqual(0.0, run18!.Value.Min, Tolerance);
        Assert.AreEqual(15.0, run18.Value.Max, Tolerance);
        Assert.AreEqual(1.0, run18.Value.Step, Tolerance);

        var withDecline = FtmsProtocol.ParseInclinationRange(Hex("E2 FF 96 00 05 00"));
        Assert.AreEqual(-3.0, withDecline!.Value.Min, Tolerance);
        Assert.AreEqual(0.5, withDecline.Value.Step, Tolerance);

        Assert.IsNull(FtmsProtocol.ParseInclinationRange(Hex("00 00 96 00")));
    }

    // ---- Treadmill Data ----

    [TestMethod]
    public void ParseTreadmillData_speed_only()
    {
        var data = FtmsProtocol.ParseTreadmillData(Hex("00 00 F4 01"));

        Assert.IsNotNull(data);
        Assert.AreEqual(5.0, data.SpeedKmh!.Value, Tolerance);
        Assert.IsNull(data.TotalDistanceMeters);
        Assert.IsNull(data.InclinePercent);
    }

    [TestMethod]
    public void ParseTreadmillData_typical_frame()
    {
        // Flags 0x048C: speed (bit 0 cleared), distance, inclination + ramp, energy, elapsed time.
        var data = FtmsProtocol.ParseTreadmillData(Hex(
            "8C 04" +
            "F4 01" +          // speed 5.00 km/h
            "2C 01 00" +       // distance 300 m
            "14 00 00 00" +    // inclination 2.0 %, ramp angle 0
            "0F 00 FF FF FF" + // energy 15 kcal, per hour / per minute unavailable
            "3C 00"));         // elapsed 60 s

        Assert.IsNotNull(data);
        Assert.AreEqual(5.0, data.SpeedKmh!.Value, Tolerance);
        Assert.AreEqual(300, data.TotalDistanceMeters);
        Assert.AreEqual(2.0, data.InclinePercent!.Value, Tolerance);
        Assert.AreEqual(15, data.TotalEnergyKcal);
        Assert.AreEqual(TimeSpan.FromSeconds(60), data.ElapsedTime);
        Assert.IsNull(data.HeartRateBpm);
    }

    [TestMethod]
    public void ParseTreadmillData_more_data_bit_means_no_speed()
    {
        // Flags 0x0101: More Data set (no speed), heart rate present.
        var data = FtmsProtocol.ParseTreadmillData(Hex("01 01 8C"));

        Assert.IsNotNull(data);
        Assert.IsNull(data.SpeedKmh);
        Assert.AreEqual(140, data.HeartRateBpm);
    }

    [TestMethod]
    public void ParseTreadmillData_unavailable_values_are_null()
    {
        // Flags 0x0088: speed, inclination 0x7FFF, energy 0xFFFF.
        var data = FtmsProtocol.ParseTreadmillData(Hex("88 00 00 00 FF 7F 00 00 FF FF FF FF FF"));

        Assert.IsNotNull(data);
        Assert.AreEqual(0.0, data.SpeedKmh!.Value, Tolerance);
        Assert.IsNull(data.InclinePercent);
        Assert.IsNull(data.TotalEnergyKcal);
    }

    [TestMethod]
    public void ParseTreadmillData_negative_inclination()
    {
        var data = FtmsProtocol.ParseTreadmillData(Hex("08 00 F4 01 F1 FF 00 00"));

        Assert.AreEqual(-1.5, data!.InclinePercent!.Value, Tolerance);
    }

    [TestMethod]
    public void ParseTreadmillData_all_fields_keeps_offsets_right()
    {
        // Flags 0x1FFE: every field present (bit 0 cleared => speed present).
        var data = FtmsProtocol.ParseTreadmillData(Hex(
            "FE 1F" +
            "E8 03" +          // speed 10.00 km/h
            "84 03" +          // average speed 9.00 km/h
            "10 27 00" +       // distance 10000 m
            "32 00 00 00" +    // inclination 5.0 %, ramp angle
            "00 00 00 00" +    // elevation gain +/-
            "00" + "00" +      // instantaneous / average pace
            "C8 00 00 00 00" + // energy 200 kcal
            "96" +             // heart rate 150
            "00" +             // metabolic equivalent
            "10 0E" +          // elapsed 3600 s
            "2C 01" +          // remaining 300 s
            "00 00 00 00"));   // force on belt, power output

        Assert.IsNotNull(data);
        Assert.AreEqual(10.0, data.SpeedKmh!.Value, Tolerance);
        Assert.AreEqual(9.0, data.AverageSpeedKmh!.Value, Tolerance);
        Assert.AreEqual(10000, data.TotalDistanceMeters);
        Assert.AreEqual(5.0, data.InclinePercent!.Value, Tolerance);
        Assert.AreEqual(200, data.TotalEnergyKcal);
        Assert.AreEqual(150, data.HeartRateBpm);
        Assert.AreEqual(TimeSpan.FromHours(1), data.ElapsedTime);
        Assert.AreEqual(TimeSpan.FromSeconds(300), data.RemainingTime);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("00")]
    [DataRow("00 00 F4")]
    [DataRow("8C 04 F4 01 2C 01 00")]
    public void ParseTreadmillData_rejects_truncated_frames(string hex)
    {
        Assert.IsNull(FtmsProtocol.ParseTreadmillData(Hex(hex)));
    }
}
