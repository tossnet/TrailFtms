using Microsoft.JSInterop;
using TrailFtms.Services.Bluetooth;

namespace TrailFtms.Tests.Fakes;

/// <summary>
/// Stands in for wwwroot/js/ftms.js: answers the Control Point like a treadmill would,
/// synchronously, by calling back into the <see cref="FtmsTreadmill"/>.
/// </summary>
public sealed class FakeFtmsJs : IJSRuntime, IJSObjectReference
{
    public string Availability { get; set; } = "available";

    /// <summary>Device name returned by the picker, null to simulate a cancelled picker.</summary>
    public string? DeviceName { get; set; } = "FS-TEST";

    public Dictionary<ushort, byte[]> Reads { get; } = new()
    {
        [0x2ACC] = Convert.FromHexString("0C12000003000000"),
        [0x2AD4] = Convert.FromHexString("640008070A00"), // 1.0 to 18.0 km/h, step 0.1
        [0x2AD5] = Convert.FromHexString("000096000A00"), // 0 to 15 %, step 1
    };

    /// <summary>Answer to each Control Point write, null for no answer. Default: success.</summary>
    public Func<byte[], byte[]?> Respond { get; set; } = command => [0x80, command[0], 0x01];

    public List<byte[]> Writes { get; } = [];

    /// <summary>Runs inside the "connect" call, e.g. to raise events a real connection would.</summary>
    public Action? DuringConnect { get; set; }

    public FtmsTreadmill Treadmill { get; }

    public FakeFtmsJs() => Treadmill = new FtmsTreadmill(this) { ResponseTimeout = TimeSpan.FromMilliseconds(50) };

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

    private string? Connect()
    {
        DuringConnect?.Invoke();
        return DeviceName;
    }

    private object? Write(byte[] command)
    {
        Writes.Add(command);
        if (Respond(command) is { } response)
            Treadmill.OnControlPoint(response);
        return null;
    }
}
