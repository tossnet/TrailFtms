using System.Buffers.Binary;

namespace TrailFtms.Services.Bluetooth;

/// <summary>
/// Encoding and decoding of the Bluetooth Fitness Machine Service (FTMS) frames used by a treadmill.
/// Pure functions, all multi-byte values are little-endian.
/// </summary>
public static class FtmsProtocol
{
    private const double SpeedResolution = 0.01;      // km/h
    private const double InclinationResolution = 0.1; // %

    // ---- Control Point commands (0x2AD9) ----

    public static byte[] RequestControl() => [(byte)ControlPointOpCode.RequestControl];

    public static byte[] Reset() => [(byte)ControlPointOpCode.Reset];

    public static byte[] StartOrResume() => [(byte)ControlPointOpCode.StartOrResume];

    public static byte[] StopOrPause(StopKind kind) => [(byte)ControlPointOpCode.StopOrPause, (byte)kind];

    /// <summary>Set Target Speed, <paramref name="kmh"/> encoded as uint16 in 0.01 km/h.</summary>
    public static byte[] SetTargetSpeed(double kmh)
    {
        var raw = Math.Round(kmh / SpeedResolution);
        ArgumentOutOfRangeException.ThrowIfLessThan(raw, ushort.MinValue, nameof(kmh));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(raw, ushort.MaxValue, nameof(kmh));

        var frame = new byte[3];
        frame[0] = (byte)ControlPointOpCode.SetTargetSpeed;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), (ushort)raw);
        return frame;
    }

    /// <summary>Set Target Inclination, <paramref name="percent"/> encoded as sint16 in 0.1 %.</summary>
    public static byte[] SetTargetInclination(double percent)
    {
        var raw = Math.Round(percent / InclinationResolution);
        ArgumentOutOfRangeException.ThrowIfLessThan(raw, short.MinValue, nameof(percent));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(raw, short.MaxValue, nameof(percent));

        var frame = new byte[3];
        frame[0] = (byte)ControlPointOpCode.SetTargetInclination;
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(1), (short)raw);
        return frame;
    }

    // ---- Control Point response (indication) ----

    /// <summary>Decodes <c>0x80, request op code, result</c>. Returns null if the frame is not a response.</summary>
    public static ControlPointResponse? ParseControlPointResponse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3 || data[0] != (byte)ControlPointOpCode.ResponseCode)
            return null;

        return new ControlPointResponse((ControlPointOpCode)data[1], (ControlPointResult)data[2]);
    }

    // ---- Fitness Machine Status (0x2ADA) ----

    /// <summary>Decodes the status op code. Parameters (new targets, stop kind) are ignored.</summary>
    public static MachineStatus? ParseMachineStatus(ReadOnlySpan<byte> data) =>
        data.IsEmpty ? null : (MachineStatus)data[0];

    // ---- Machine capabilities ----

    /// <summary>Decodes Fitness Machine Feature (0x2ACC): two uint32 bit fields.</summary>
    public static MachineFeatures? ParseMachineFeatures(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            return null;

        return new MachineFeatures(
            (FitnessMachineFeatures)BinaryPrimitives.ReadUInt32LittleEndian(data),
            (TargetSettingFeatures)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]));
    }

    /// <summary>Decodes Supported Speed Range (0x2AD4): min, max, step as uint16 in 0.01 km/h.</summary>
    public static SupportedRange? ParseSpeedRange(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
            return null;

        return new SupportedRange(
            BinaryPrimitives.ReadUInt16LittleEndian(data) * SpeedResolution,
            BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) * SpeedResolution,
            BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) * SpeedResolution);
    }

    /// <summary>Decodes Supported Inclination Range (0x2AD5): min, max as sint16 and step as uint16, in 0.1 %.</summary>
    public static SupportedRange? ParseInclinationRange(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
            return null;

        return new SupportedRange(
            BinaryPrimitives.ReadInt16LittleEndian(data) * InclinationResolution,
            BinaryPrimitives.ReadInt16LittleEndian(data[2..]) * InclinationResolution,
            BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) * InclinationResolution);
    }

    // ---- Treadmill Data (0x2ACD) ----

    [Flags]
    private enum TreadmillFlags : ushort
    {
        MoreData = 1 << 0, // inverted: instantaneous speed is present when this bit is 0
        AverageSpeed = 1 << 1,
        TotalDistance = 1 << 2,
        InclinationAndRampAngle = 1 << 3,
        ElevationGain = 1 << 4,
        InstantaneousPace = 1 << 5,
        AveragePace = 1 << 6,
        ExpendedEnergy = 1 << 7,
        HeartRate = 1 << 8,
        MetabolicEquivalent = 1 << 9,
        ElapsedTime = 1 << 10,
        RemainingTime = 1 << 11,
        ForceOnBeltAndPowerOutput = 1 << 12,
    }

    // Size in bytes of the field(s) announced by each flag bit, in frame order (FTMS spec, Treadmill Data).
    private static ReadOnlySpan<byte> TreadmillFieldSizes => [2, 2, 3, 4, 4, 1, 1, 5, 1, 1, 2, 2, 4];

    private const short InclinationNotAvailable = 0x7FFF;
    private const ushort EnergyNotAvailable = 0xFFFF;

    /// <summary>
    /// Decodes a Treadmill Data notification. Returns null if the frame is shorter than its flags announce.
    /// </summary>
    public static TreadmillData? ParseTreadmillData(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return null;

        var flags = (TreadmillFlags)BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (data.Length < 2 + PayloadLength(flags))
            return null;

        var p = data[2..];
        double? speed = null, averageSpeed = null, incline = null;
        int? distance = null, energy = null, heartRate = null;
        TimeSpan? elapsed = null, remaining = null;

        if (!flags.HasFlag(TreadmillFlags.MoreData))
        {
            speed = BinaryPrimitives.ReadUInt16LittleEndian(p) * SpeedResolution;
            p = p[2..];
        }
        if (flags.HasFlag(TreadmillFlags.AverageSpeed))
        {
            averageSpeed = BinaryPrimitives.ReadUInt16LittleEndian(p) * SpeedResolution;
            p = p[2..];
        }
        if (flags.HasFlag(TreadmillFlags.TotalDistance))
        {
            distance = p[0] | p[1] << 8 | p[2] << 16;
            p = p[3..];
        }
        if (flags.HasFlag(TreadmillFlags.InclinationAndRampAngle))
        {
            var raw = BinaryPrimitives.ReadInt16LittleEndian(p);
            if (raw != InclinationNotAvailable)
                incline = raw * InclinationResolution;
            p = p[4..]; // inclination + ramp angle (ignored)
        }
        if (flags.HasFlag(TreadmillFlags.ElevationGain))
            p = p[4..];
        if (flags.HasFlag(TreadmillFlags.InstantaneousPace))
            p = p[1..];
        if (flags.HasFlag(TreadmillFlags.AveragePace))
            p = p[1..];
        if (flags.HasFlag(TreadmillFlags.ExpendedEnergy))
        {
            var raw = BinaryPrimitives.ReadUInt16LittleEndian(p);
            if (raw != EnergyNotAvailable)
                energy = raw;
            p = p[5..]; // total + per hour + per minute (ignored)
        }
        if (flags.HasFlag(TreadmillFlags.HeartRate))
        {
            heartRate = p[0];
            p = p[1..];
        }
        if (flags.HasFlag(TreadmillFlags.MetabolicEquivalent))
            p = p[1..];
        if (flags.HasFlag(TreadmillFlags.ElapsedTime))
        {
            elapsed = TimeSpan.FromSeconds(BinaryPrimitives.ReadUInt16LittleEndian(p));
            p = p[2..];
        }
        if (flags.HasFlag(TreadmillFlags.RemainingTime))
            remaining = TimeSpan.FromSeconds(BinaryPrimitives.ReadUInt16LittleEndian(p));

        return new TreadmillData
        {
            SpeedKmh = speed,
            AverageSpeedKmh = averageSpeed,
            TotalDistanceMeters = distance,
            InclinePercent = incline,
            TotalEnergyKcal = energy,
            HeartRateBpm = heartRate,
            ElapsedTime = elapsed,
            RemainingTime = remaining,
        };
    }

    private static int PayloadLength(TreadmillFlags flags)
    {
        // Bit 0 is inverted: flip it so that every set bit means "field present".
        var present = (int)flags ^ (int)TreadmillFlags.MoreData;
        var sizes = TreadmillFieldSizes;
        var length = 0;
        for (var bit = 0; bit < sizes.Length; bit++)
        {
            if ((present & (1 << bit)) != 0)
                length += sizes[bit];
        }
        return length;
    }
}
