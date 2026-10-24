using TrailFtms.Services.Bluetooth;

namespace TrailFtms.Tests.Services.Bluetooth;

[TestClass]
public sealed class DomyosProtocolTests
{
    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    /// <summary>Measurement frame: speed 21.5 km/h, 78 rpm, 42 kcal, 3.4 km, level 6, 128 bpm.</summary>
    private static byte[] DataFrame()
    {
        var parameters = new byte[23];
        parameters.AsSpan().Fill(0xFF);
        parameters[4] = 0x00; // speed, 0.1 km/h (frame bytes 6-7)
        parameters[5] = 0xD7;
        parameters[7] = 78;   // cadence (frame byte 9)
        parameters[8] = 0x00; // energy (frame bytes 10-11)
        parameters[9] = 42;
        parameters[10] = 0x00; // distance, 0.1 km (frame bytes 12-13)
        parameters[11] = 34;
        parameters[12] = 6;   // resistance (frame byte 14)
        parameters[16] = 128; // heart rate (frame byte 18)
        return DomyosProtocol.Frame(0xBC, parameters);
    }

    // Frames captured with nRF Connect on a Domyos E-Seat (DOMYOS-BIKE-1810).
    [TestMethod]
    [DataRow("F0 C8 01 B9")]
    [DataRow("F0 C9 B9")]
    [DataRow("F0 AC 9C")]
    public void Frame_appends_the_checksum(string captured)
    {
        var bytes = Hex(captured);

        CollectionAssert.AreEqual(bytes, DomyosProtocol.Frame(bytes[1], bytes.AsSpan(2, bytes.Length - 3)));
    }

    [TestMethod]
    [DataRow("F0 D8 01 C9")]
    [DataRow("F0 D9 00 08 36 81 68 F0")]
    public void Captured_answers_are_valid(string captured) =>
        Assert.IsTrue(DomyosProtocol.IsValid(Hex(captured)));

    [TestMethod]
    [DataRow("F0 D8 01 C8")]
    [DataRow("A6 FF 00 00 01 51")] // second half of a measurement frame alone
    [DataRow("F0 C9")]
    public void Wrong_or_partial_frames_are_invalid(string bytes) =>
        Assert.IsFalse(DomyosProtocol.IsValid(Hex(bytes)));

    [TestMethod]
    public void Init_starts_with_the_captured_commands()
    {
        CollectionAssert.AreEqual(Hex("F0 C8 01 B9"), DomyosProtocol.Init[0]);
        CollectionAssert.AreEqual(Hex("F0 C9 B9"), DomyosProtocol.Init[1]);
        CollectionAssert.AreEqual(Hex("F0 AC 9C"), DomyosProtocol.RequestData());
    }

    [TestMethod]
    public void SetResistance_writes_the_level_and_checksum()
    {
        CollectionAssert.AreEqual(
            Hex("F0 AD FF FF FF FF FF FF FF FF 0A FF FF FF FF FF FF 00 01 FF FF FF 97"),
            DomyosProtocol.SetResistance(10));
        Assert.AreEqual(0x90, DomyosProtocol.SetResistance(3)[^1]);
    }

    [TestMethod]
    public void SetResistance_rejects_levels_out_of_range()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DomyosProtocol.SetResistance(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DomyosProtocol.SetResistance(16));
    }

    [TestMethod]
    [DataRow(0.0, 1)]
    [DataRow(4.0, 5)]
    [DataRow(4.4, 5)]
    [DataRow(-3.0, 1)]
    [DataRow(30.0, 15)]
    public void LevelFor_simulates_one_percent_per_level(double incline, int level) =>
        Assert.AreEqual(level, DomyosProtocol.LevelFor(incline));

    [TestMethod]
    public void ParseData_decodes_the_measurements()
    {
        var data = DomyosProtocol.ParseData(DataFrame())!;

        Assert.AreEqual(21.5, data.SpeedKmh!.Value, 1e-9);
        Assert.AreEqual(78, data.CadenceRpm);
        Assert.AreEqual(42, data.TotalEnergyKcal);
        Assert.AreEqual(3400, data.TotalDistanceMeters);
        Assert.AreEqual(6, data.ResistanceLevel);
        Assert.AreEqual(5.0, data.InclinePercent!.Value, 1e-9);
        Assert.AreEqual(128, data.HeartRateBpm);
    }

    [TestMethod]
    public void ParseData_leaves_missing_values_null()
    {
        var frame = DataFrame();
        frame[10] = 0xFF; // energy not available
        frame[18] = 0;    // no heart rate
        frame[^1] = DomyosProtocol.Frame(frame[1], frame.AsSpan(2, frame.Length - 3))[^1];

        var data = DomyosProtocol.ParseData(frame)!;

        Assert.IsNull(data.TotalEnergyKcal);
        Assert.IsNull(data.HeartRateBpm);
    }

    [TestMethod]
    public void ParseData_ignores_other_and_corrupted_frames()
    {
        var corrupted = DataFrame();
        corrupted[9]++;

        Assert.IsNull(DomyosProtocol.ParseData(corrupted));
        Assert.IsNull(DomyosProtocol.ParseData(Hex("F0 D8 01 C9")));
    }

    [TestMethod]
    public void Assembler_joins_the_two_parts_of_a_measurement_frame()
    {
        var frame = DataFrame();
        var assembler = new DomyosFrameAssembler();

        Assert.IsNull(assembler.Push(frame.AsSpan(0, 20)));
        CollectionAssert.AreEqual(frame, assembler.Push(frame.AsSpan(20)));
        CollectionAssert.AreEqual(Hex("F0 D8 01 C9"), assembler.Push(Hex("F0 D8 01 C9")));
    }
}
