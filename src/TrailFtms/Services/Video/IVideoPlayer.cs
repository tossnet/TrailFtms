using Microsoft.AspNetCore.Components;

namespace TrailFtms.Services.Video;

/// <summary>Player state. Values match the YouTube IFrame API, plus <see cref="NotLoaded"/>.</summary>
public enum VideoState
{
    NotLoaded = -2,
    Unstarted = -1,
    Ended = 0,
    Playing = 1,
    Paused = 2,
    Buffering = 3,
    Cued = 5,
}

public enum VideoError
{
    InvalidId,
    PlaybackFailed,
    NotFound,
    /// <summary>The owner does not allow the video to be played outside YouTube.</summary>
    NotEmbeddable,
}

/// <summary>Video player whose time drives the treadmill.</summary>
public interface IVideoPlayer : IAsyncDisposable
{
    VideoState State { get; }
    string? VideoId { get; }

    event Action? StateChanged;
    event Action<VideoError>? ErrorOccurred;

    /// <summary>Creates the player inside <paramref name="container"/> (or reuses it) and cues the video.</summary>
    Task LoadAsync(ElementReference container, string videoId);

    /// <summary>Destroys the player, to call when its container leaves the page.</summary>
    Task UnloadAsync();

    /// <summary>Current position in the video, in seconds (0 when nothing is loaded).</summary>
    ValueTask<double> GetCurrentTimeAsync();

    /// <summary>Video duration in seconds (0 until the metadata is known).</summary>
    ValueTask<double> GetDurationAsync();

    /// <summary>Title of the loaded video (empty until it is known).</summary>
    ValueTask<string> GetTitleAsync();

    Task PlayAsync();
    Task PauseAsync();
    Task SeekAsync(double seconds);
}
