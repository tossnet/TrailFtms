using Microsoft.JSInterop;
using TrailFtms.Services.Bluetooth;

namespace TrailFtms.Tests.Fakes;

/// <summary>
/// Stands in for wwwroot/js/ftms.js: answers the Control Point like a treadmill would (or the console
/// like a Domyos bike, see <see cref="Protocol"/>), synchronously, by calling back into the <see cref="FtmsTreadmill"/>.
/// </summary>
public sealed class FakeFtmsJs : IJSRuntime, IJSObjectReference
{
    public string Availability { get; set; } = "available";

    /// <summary>Device name returned by the picker, null to simulate a cancelled picker.</summary>
    public string? DeviceName { get; set; } = "FS-TEST";

    /// <summary>"ftms" for a treadmill, "domyos" for a bike.</summary>
    public string Protocol { get; set; } = "ftms";

    public Dictionary<ushort, byte[]> Reads { get; } = new()
    {
        [0x2ACC] = Convert.FromHexString("0C12000003000000"),
        [0x2AD4] = Convert.FromHexString("640008070A00"), // 1.0 to 18.0 km/h, step 0.1
        [0x2AD5] = Convert.FromHexString("000096000A00"), // 0 to 15 %, step 1
    };

    /// <summary>
    /// Answer to each write, null for no answer. Default: success, i.e. the Control Point response,
    /// or for a bike the console frame with op code + 0x10.
    /// </summary>
    public Func<byte[], byte[]?> Respond { get; set; } = command => command[0] == 0xF0
        ? DomyosProtocol.Frame((byte)(command[1] + 0x10))
        : [0x80, command[0], 0x01];

    public List<byte[]> Writes { get; } = [];

    /// <summary>Runs inside the "connect" call, e.g. to raise events a real connection would.</summary>
    public Action? DuringConnect { get; set; }

    public FtmsTreadmill Treadmill { get; }

    public FakeFtmsJs() => Treadmill = new FtmsTreadmill(this)
    {
        ResponseTimeout = TimeSpan.FromMilliseconds(50),
        PollInterval = TimeSpan.Zero,
    };

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        object? result = identifier switch
        {
            "import" => this,
            "getAvailability" => Availability,
            "connect" => Connect(),
            "read" => Reads.GetValueOrDefault((ushort)args![0]!),
            "write" => Write((byte[])args![0]!),
            "disconnect" => null,
            _ => throw new InvalidOperationException($"Unexpected JS call '{identifier}'."),
        };
        return ValueTask.FromResult((TValue)result!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ConnectedDevice? Connect()
    {
        DuringConnect?.Invoke();
        return DeviceName is null ? null : new ConnectedDevice(DeviceName, Protocol);
    }

    private object? Write(byte[] command)
    {
        Writes.Add(command);
        if (Respond(command) is not { } response)
            return null;
        if (Protocol == ConnectedDevice.Domyos)
            Treadmill.OnDomyosData(response);
        else
            Treadmill.OnControlPoint(response);
        return null;
    }
}
