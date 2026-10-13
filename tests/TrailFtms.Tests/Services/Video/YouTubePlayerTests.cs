using Bunit;
using TrailFtms.Services.Video;

namespace TrailFtms.Tests.Services.Video;

[TestClass]
public sealed class YouTubePlayerTests
{
    [TestMethod]
    [DataRow(-1, VideoState.Unstarted)]
    [DataRow(0, VideoState.Ended)]
    [DataRow(1, VideoState.Playing)]
    [DataRow(2, VideoState.Paused)]
    [DataRow(3, VideoState.Buffering)]
    [DataRow(5, VideoState.Cued)]
    public void YouTube_states_are_mapped(int raw, VideoState expected)
    {
        using var ctx = new BunitContext();
        var player = new YouTubePlayer(ctx.JSInterop.JSRuntime);
        var changes = 0;
        player.StateChanged += () => changes++;

        player.OnStateChange(raw);

        Assert.AreEqual(expected, player.State);
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void Unknown_state_and_repeated_state_are_ignored()
    {
        using var ctx = new BunitContext();
        var player = new YouTubePlayer(ctx.JSInterop.JSRuntime);
        player.OnStateChange(1);
        var changes = 0;
        player.StateChanged += () => changes++;

        player.OnStateChange(1);
        player.OnStateChange(42);

        Assert.AreEqual(VideoState.Playing, player.State);
        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    [DataRow(2, VideoError.InvalidId)]
    [DataRow(5, VideoError.PlaybackFailed)]
    [DataRow(100, VideoError.NotFound)]
    [DataRow(101, VideoError.NotEmbeddable)]
    [DataRow(150, VideoError.NotEmbeddable)]
    public void YouTube_errors_are_mapped(int code, VideoError expected)
    {
        using var ctx = new BunitContext();
        var player = new YouTubePlayer(ctx.JSInterop.JSRuntime);
        VideoError? error = null;
        player.ErrorOccurred += e => error = e;

        player.OnError(code);

        Assert.AreEqual(expected, error);
    }

    [TestMethod]
    public async Task Nothing_loaded_returns_zero_and_ignores_commands()
    {
        using var ctx = new BunitContext();
        var player = new YouTubePlayer(ctx.JSInterop.JSRuntime);

        Assert.AreEqual(0d, await player.GetCurrentTimeAsync());
        Assert.AreEqual(0d, await player.GetDurationAsync());
        await player.PlayAsync();
        await player.SeekAsync(30);
        Assert.AreEqual(VideoState.NotLoaded, player.State);
    }
}
