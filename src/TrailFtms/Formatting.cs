namespace TrailFtms;

public static class Formatting
{
    /// <summary>Video time as m:ss, or h:mm:ss from one hour.</summary>
    public static string Time(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Floor(Math.Max(0, seconds)));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}
