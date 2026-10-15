// Thin wrapper around the YouTube IFrame Player API.
// create() returns the YT.Player itself: C# calls its methods directly (getCurrentTime, seekTo...).

let apiReady = null;

function loadApi() {
    apiReady ??= new Promise((resolve, reject) => {
        if (window.YT?.Player) return resolve();
        const previous = window.onYouTubeIframeAPIReady;
        window.onYouTubeIframeAPIReady = () => {
            previous?.();
            resolve();
        };
        const script = document.createElement("script");
        script.src = "https://www.youtube.com/iframe_api";
        script.onerror = () => {
            apiReady = null;
            reject(new Error("YouTube IFrame API failed to load"));
        };
        document.head.appendChild(script);
    });
    return apiReady;
}

// getVideoData() is not part of the documented API: no title rather than an error if it goes away.
export function title(player) {
    return player.getVideoData?.()?.title ?? "";
}

export async function create(container, videoId, dotnet) {
    await loadApi();
    // YT.Player replaces its target element: give it a child so Blazor keeps owning the container.
    const target = document.createElement("div");
    container.replaceChildren(target);

    return new Promise(resolve => {
        const player = new YT.Player(target, {
            width: "100%",
            height: "100%",
            videoId,
            playerVars: {
                disablekb: 1,   // keep keyboard shortcuts (STOP) for the app
                playsinline: 1, // no forced fullscreen on mobile
                rel: 0,
            },
            events: {
                onReady: () => resolve(player),
                onStateChange: e => dotnet.invokeMethodAsync("OnStateChange", e.data),
                onError: e => dotnet.invokeMethodAsync("OnError", e.data),
            },
        });
    });
}
