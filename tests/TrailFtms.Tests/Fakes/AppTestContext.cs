using Blazor.WinOld;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TrailFtms.Services.Bluetooth;
using TrailFtms.Services.Video;

namespace TrailFtms.Tests.Fakes;

/// <summary>bUnit context wired like Program.cs, with the treadmill backed by <see cref="FakeFtmsJs"/>.</summary>
public sealed class AppTestContext : BunitContext
{
    public FakeFtmsJs Ftms { get; } = new();
    public FakeVideoPlayer Video { get; } = new();

    public AppTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddWinOldComponents();
        Services.AddLocalization();
        Services.AddSingleton<IFtmsTreadmill>(Ftms.Treadmill);
        Services.AddSingleton<IVideoPlayer>(Video);
    }
}
