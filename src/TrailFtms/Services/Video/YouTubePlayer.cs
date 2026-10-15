using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TrailFtms.Services.Video;

/// <summary>YouTube IFrame player (wwwroot/js/youtube.js), its methods are called directly on the YT.Player.</summary>
public sealed class YouTubePlayer(IJSRuntime js) : IVideoPlayer
{
    private IJSObjectReference? _module;
    private IJSObjectReference? _player;
    private DotNetObjectReference<YouTubePlayer>? _selfRef;

    public VideoState State { get; private set; } = VideoState.NotLoaded;
    public string? VideoId { get; private set; }

    public event Action? StateChanged;
    public event Action<VideoError>? ErrorOccurred;

    public async Task LoadAsync(ElementReference container, string videoId)
    {
        if (_player is null)
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/youtube.js");
            _selfRef ??= DotNetObjectReference.Create(this);
            _player = await _module.InvokeAsync<IJSObjectReference>("create", container, videoId, _selfRef);
        }
        else
        {
            await _player.InvokeVoidAsync("cueVideoById", videoId);
        }

        VideoId = videoId;
        SetState(VideoState.Cued);
    }

    public async Task UnloadAsync()
    {
        if (_player is null)
            return;

        var player = _player;
        _player = null;
        VideoId = null;
        try
        {
            await player.InvokeVoidAsync("destroy");
            await player.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        SetState(VideoState.NotLoaded);
    }

    public ValueTask<double> GetCurrentTimeAsync() =>
        _player?.InvokeAsync<double>("getCurrentTime") ?? ValueTask.FromResult(0d);

    public ValueTask<double> GetDurationAsync() =>
        _player?.InvokeAsync<double>("getDuration") ?? ValueTask.FromResult(0d);

    public ValueTask<string> GetTitleAsync() =>
        _player is not null && _module is not null ? _module.InvokeAsync<string>("title", _player) : ValueTask.FromResult("");

    public Task PlayAsync() => InvokeAsync("playVideo");

    public Task PauseAsync() => InvokeAsync("pauseVideo");

    public Task SeekAsync(double seconds) => InvokeAsync("seekTo", Math.Max(0, seconds), true);

    // ---- Callbacks from youtube.js ----

    [JSInvokable]
    public void OnStateChange(int state)
    {
        if (Enum.IsDefined((VideoState)state))
            SetState((VideoState)state);
    }

    [JSInvokable]
    public void OnError(int code) => ErrorOccurred?.Invoke(code switch
    {
        2 => VideoError.InvalidId,
        100 => VideoError.NotFound,
        101 or 150 => VideoError.NotEmbeddable,
        _ => VideoError.PlaybackFailed,
    });

    private async Task InvokeAsync(string method, params object?[] args)
    {
        if (_player is not null)
            await _player.InvokeVoidAsync(method, args);
    }

    private void SetState(VideoState state)
    {
        if (State == state)
            return;
        State = state;
        StateChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        await UnloadAsync();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
        _selfRef?.Dispose();
    }
}
