using TrailFtms.Services.Video;

namespace TrailFtms.Tests.Services.Video;

[TestClass]
public sealed class YouTubeUrlTests
{
    [TestMethod]
    [DataRow("KiEN4HxM4ic")]
    [DataRow("  KiEN4HxM4ic  ")]
    [DataRow("https://www.youtube.com/watch?v=KiEN4HxM4ic")]
    [DataRow("https://www.youtube.com/watch?v=KiEN4HxM4ic&t=120s")]
    [DataRow("https://www.youtube.com/watch?feature=share&v=KiEN4HxM4ic")]
    [DataRow("www.youtube.com/watch?v=KiEN4HxM4ic")]
    [DataRow("youtube.com/watch?v=KiEN4HxM4ic")]
    [DataRow("https://m.youtube.com/watch?v=KiEN4HxM4ic")]
    [DataRow("https://youtu.be/KiEN4HxM4ic")]
    [DataRow("https://youtu.be/KiEN4HxM4ic?si=abcdef&t=30")]
    [DataRow("https://www.youtube.com/shorts/KiEN4HxM4ic")]
    [DataRow("https://www.youtube.com/embed/KiEN4HxM4ic?start=10")]
    [DataRow("https://www.youtube.com/live/KiEN4HxM4ic")]
    [DataRow("https://www.youtube-nocookie.com/embed/KiEN4HxM4ic")]
    public void Extracts_video_id(string input)
    {
        Assert.AreEqual("KiEN4HxM4ic", YouTubeUrl.ParseVideoId(input));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("KiEN4HxM4i")]                                   // 10 characters
    [DataRow("KiEN4HxM4ic!")]
    [DataRow("https://www.youtube.com/watch?v=short")]
    [DataRow("https://www.youtube.com/watch?list=PL123")]
    [DataRow("https://www.youtube.com/@channel")]
    [DataRow("https://vimeo.com/KiEN4HxM4ic")]
    [DataRow("https://notyoutube.com/watch?v=KiEN4HxM4ic")]
    public void Rejects_invalid_input(string? input)
    {
        Assert.IsNull(YouTubeUrl.ParseVideoId(input));
    }
}
