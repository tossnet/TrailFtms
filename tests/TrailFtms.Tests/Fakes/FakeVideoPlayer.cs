using Microsoft.AspNetCore.Components;
using TrailFtms.Services.Video;

namespace TrailFtms.Tests.Fakes;

/// <summary>Video player driven by the test: set <see cref="CurrentTime"/> and call <see cref="SetState"/>.</summary>
public sealed class FakeVideoPlayer : IVideoPlayer
{
    public VideoState State { get; private set; } = VideoState.NotLoaded;
    public string? VideoId { get; private set; }
    public double CurrentTime { get; set; }
    public double Duration { get; set; }
    public string Title { get; set; } = "";
    public List<string> Loads { get; } = [];

    public event Action? StateChanged;
    public event Action<VideoError>? ErrorOccurred;

    public Task LoadAsync(ElementReference container, string videoId)
    {
        Loads.Add(videoId);
        VideoId = videoId;
        SetState(VideoState.Cued);
        return Task.CompletedTask;
    }

    public Task UnloadAsync()
    {
        VideoId = null;
        SetState(VideoState.NotLoaded);
        return Task.CompletedTask;
    }

    public ValueTask<double> GetCurrentTimeAsync() => ValueTask.FromResult(CurrentTime);
    public ValueTask<double> GetDurationAsync() => ValueTask.FromResult(Duration);
    public ValueTask<string> GetTitleAsync() => ValueTask.FromResult(Title);

    public Task PlayAsync()
    {
        SetState(VideoState.Playing);
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        SetState(VideoState.Paused);
        return Task.CompletedTask;
    }

    public Task SeekAsync(double seconds)
    {
        CurrentTime = seconds;
        return Task.CompletedTask;
    }

    public void SetState(VideoState state)
    {
        State = state;
        StateChanged?.Invoke();
    }

    public void RaiseError(VideoError error) => ErrorOccurred?.Invoke(error);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
