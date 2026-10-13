using System.Text.RegularExpressions;

namespace TrailFtms.Services.Video;

/// <summary>Extracts a YouTube video id from a raw id or any common YouTube link.</summary>
public static partial class YouTubeUrl
{
    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoIdRegex();

    /// <summary>
    /// Accepts <c>KiEN4HxM4ic</c>, <c>youtube.com/watch?v=</c>, <c>youtu.be/</c>,
    /// <c>/shorts/</c>, <c>/embed/</c>, <c>/live/</c> (with or without scheme and extra parameters).
    /// Returns null when no valid id is found.
    /// </summary>
    public static string? ParseVideoId(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        if (IsVideoId(text))
            return text;

        if (!text.Contains("://"))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        string? candidate = null;
        if (host == "youtu.be")
        {
            candidate = segments.FirstOrDefault();
        }
        else if (host == "youtube.com" || host.EndsWith(".youtube.com") || host == "youtube-nocookie.com" || host.EndsWith(".youtube-nocookie.com"))
        {
            candidate = segments switch
            {
                ["watch", ..] => QueryValue(uri.Query, "v"),
                ["shorts" or "embed" or "live" or "v", var id, ..] => id,
                _ => null,
            };
        }

        return candidate is not null && IsVideoId(candidate) ? candidate : null;
    }

    private static bool IsVideoId(string value) => VideoIdRegex().IsMatch(value);

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator > 0 && pair.AsSpan(0, separator).SequenceEqual(key))
                return Uri.UnescapeDataString(pair[(separator + 1)..]);
        }
        return null;
    }
}
