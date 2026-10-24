namespace TrailFtms.Services.Bluetooth;

/// <summary>
/// Encoding and decoding of the proprietary Domyos protocol (Decathlon bikes without FTMS, e.g. E-Seat),
/// carried over a Microchip "transparent UART" service. Pure functions.
/// Every frame is <c>0xF0</c>, op code, parameters, then a checksum: the sum of the previous bytes modulo 256.
/// The machine answers each command with op code + 0x10 (<c>F0 C8 01 B9</c> gives <c>F0 D8 01 C9</c>).
/// Multi-byte values are big-endian.
/// </summary>
/// <remarks>
/// The bike has no slope: the program inclination is simulated by the resistance, one level per %
/// (0 % is the lowest level). The ride engine and the programs keep working in %.
/// </remarks>
public static class DomyosProtocol
{
    private const byte Start = 0xF0;
    private const byte RequestDataOpCode = 0xAC;
    private const byte DataOpCode = 0xBC;
    private const byte SetResistanceOpCode = 0xAD;

    /// <summary>Resistance levels of the Domyos bikes.</summary>
    public const int MinLevel = 1;
    public const int MaxLevel = 15;

    /// <summary>Length of the measurement frame, notified in two parts (20 + 6 bytes).</summary>
    internal const int DataLength = 26;

    /// <summary>The inclination the resistance can simulate, in %.</summary>
    public static SupportedRange SimulatedInclineRange { get; } = new(0, MaxLevel - MinLevel, 1);

    /// <summary>Resistance level simulating <paramref name="inclinePercent"/>, see <see cref="SimulatedInclineRange"/>.</summary>
    public static int LevelFor(double inclinePercent) =>
        (int)SimulatedInclineRange.Clamp(inclinePercent) + MinLevel;

    // ---- Commands ----

    /// <summary>Sent once after connecting, each answered by the console.</summary>
    public static IReadOnlyList<byte[]> Init { get; } =
        [Frame(0xC8, 0x01), Frame(0xC9), Frame(0xA3), Frame(0xA4), Frame(0xA5), Frame(0xAB), Frame(0xC4, 0x03)];

    /// <summary>Asks for the measurements: the console only sends them as an answer, so this is polled.</summary>
    public static byte[] RequestData() => Frame(RequestDataOpCode);

    /// <summary>
    /// Sets the resistance level. The console frame carries display fields as well, 0xFF leaves them unchanged.
    /// 23 bytes: longer than one write, the JS layer splits it.
    /// </summary>
    public static byte[] SetResistance(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, MinLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, MaxLevel);

        var parameters = new byte[20];
        parameters.AsSpan().Fill(0xFF);
        parameters[8] = (byte)level;
        parameters[15] = 0x00;
        parameters[16] = 0x01;
        return Frame(SetResistanceOpCode, parameters);
    }

    /// <summary>Builds <c>F0, op code, parameters, checksum</c>.</summary>
    public static byte[] Frame(byte opCode, params ReadOnlySpan<byte> parameters)
    {
        var frame = new byte[parameters.Length + 3];
        frame[0] = Start;
        frame[1] = opCode;
        parameters.CopyTo(frame.AsSpan(2));
        frame[^1] = Checksum(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }

    // ---- Answers ----

    /// <summary>A complete frame: starts with F0 and ends with the right checksum.</summary>
    public static bool IsValid(ReadOnlySpan<byte> frame) =>
        frame.Length >= 3 && frame[0] == Start && frame[^1] == Checksum(frame[..^1]);

    private const byte EnergyNotAvailable = 0xFF;

    /// <summary>
    /// Decodes the measurement frame (answer to <see cref="RequestData"/>), null for any other frame.
    /// The inclination is the one simulated by the resistance level.
    /// </summary>
    public static TreadmillData? ParseData(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != DataLength || frame[1] != DataOpCode || !IsValid(frame))
            return null;

        var level = Math.Clamp((int)frame[14], MinLevel, MaxLevel);
        return new TreadmillData
        {
            SpeedKmh = (frame[6] << 8 | frame[7]) / 10.0,
            CadenceRpm = frame[9],
            TotalEnergyKcal = frame[10] == EnergyNotAvailable ? null : frame[10] << 8 | frame[11],
            TotalDistanceMeters = (frame[12] << 8 | frame[13]) * 100, // 0.1 km
            ResistanceLevel = level,
            InclinePercent = level - MinLevel,
            HeartRateBpm = frame[18] is 0 or 0xFF ? null : frame[18],
        };
    }

    private static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        var sum = 0;
        foreach (var b in bytes)
            sum += b;
        return (byte)sum;
    }
}

/// <summary>
/// Rebuilds frames from notifications: one write carries at most 20 bytes, so the measurement frame
/// comes in two notifications (20 + 6 bytes). Any other notification is a whole frame.
/// </summary>
public sealed class DomyosFrameAssembler
{
    private const int ChunkLength = 20;

    // First parts of a long frame: measurements, and their variants seen on other Domyos consoles.
    private static ReadOnlySpan<byte> LongFrameOpCodes => [0xBC, 0xDB, 0xDD];

    private byte[]? _head;

    /// <summary>Feeds a notification. Returns the frame it completes, or null while waiting for its second part.</summary>
    public byte[]? Push(ReadOnlySpan<byte> chunk)
    {
        if (_head is { } head)
        {
            _head = null;
            return [.. head, .. chunk];
        }
        if (chunk.Length == ChunkLength && chunk[0] == 0xF0 && LongFrameOpCodes.Contains(chunk[1]))
        {
            _head = chunk.ToArray();
            return null;
        }
        return chunk.ToArray();
    }

    /// <summary>Forgets a pending first part (new connection).</summary>
    public void Reset() => _head = null;
}
