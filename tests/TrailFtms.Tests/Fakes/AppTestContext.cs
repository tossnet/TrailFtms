using Blazor.WinOld;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TrailFtms.Services.Bluetooth;
using TrailFtms.Services.Programs;
using TrailFtms.Services.Settings;
using TrailFtms.Services.Storage;
using TrailFtms.Services.Video;

namespace TrailFtms.Tests.Fakes;

/// <summary>bUnit context wired like Program.cs, with the treadmill backed by <see cref="FakeFtmsJs"/>.</summary>
public sealed class AppTestContext : BunitContext
{
    public FakeFtmsJs Ftms { get; } = new();
    public FakeVideoPlayer Video { get; } = new();
    public FakeBrowserStorage Storage { get; } = new();

    /// <summary>Clock of the pages and the ride engine: time only moves when a test calls Advance.</summary>
    public FakeTimeProvider Time { get; } = new();

    public AppTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWinOldComponents();
        Services.AddLocalization();
        Services.AddSingleton<IFtmsTreadmill>(Ftms.Treadmill);
        Services.AddSingleton<IVideoPlayer>(Video);
        Services.AddSingleton(WwwrootHttpHandler.CreateClient());
        Services.AddSingleton<IBrowserStorage>(Storage);
        Services.AddSingleton<TimeProvider>(Time);
        Services.AddSingleton<ProgramStore>();
        Services.AddSingleton<SettingsStore>();
    }
}
