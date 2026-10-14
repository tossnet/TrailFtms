namespace TrailFtms.Services.Programs;

/// <summary>
/// Effort of a program computed from its segments: ACSM oxygen cost equations (speed and grade),
/// level 1 (easy) to 5 (very hard) from the worst of average intensity and total volume.
/// The user speed factor is not applied, so levels compare programs between them.
/// </summary>
public readonly record struct ProgramDifficulty(
    int Level, double DistanceKm, double ElevationGainM, double MaxIncline, double AverageMet, double MetMinutes)
{
    public const int MaxLevel = 5;

    private const double RunningSpeedKmh = 8.0;
    private const double RestingVo2 = 3.5; // ml/kg/min, 1 MET

    // Upper bounds of levels 1 to 4, level 5 above.
    private static readonly double[] MetLevels = [4, 6, 8, 10];
    private static readonly double[] MetMinuteLevels = [150, 300, 500, 750];

    public static ProgramDifficulty Of(RideProgram program)
    {
        var segments = program.Segments;
        double meters = 0, gain = 0, maxIncline = 0, metMinutes = 0, seconds = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            maxIncline = Math.Max(maxIncline, segment.Incline);

            var end = i + 1 < segments.Count ? segments[i + 1].T : program.DurationSeconds;
            var duration = end - segment.T;
            if (duration <= 0)
                continue; // last segment of a program with unknown duration

            var distance = segment.Speed / 3.6 * duration;
            meters += distance;
            gain += distance * Math.Max(0, segment.Incline) / 100;
            metMinutes += Met(segment.Speed, segment.Incline) * duration / 60;
            seconds += duration;
        }

        var averageMet = seconds > 0 ? metMinutes / (seconds / 60) : 0;
        var level = Math.Max(LevelOf(averageMet, MetLevels), LevelOf(metMinutes, MetMinuteLevels));
        return new(level, meters / 1000, gain, maxIncline, averageMet, metMinutes);
    }

    /// <summary>ACSM walking / running equations. Downhill counts as flat.</summary>
    internal static double Met(double speedKmh, double incline)
    {
        var metersPerMinute = speedKmh * 1000 / 60;
        var grade = Math.Max(0, incline) / 100;
        var vo2 = speedKmh < RunningSpeedKmh
            ? RestingVo2 + 0.1 * metersPerMinute + 1.8 * metersPerMinute * grade
            : RestingVo2 + 0.2 * metersPerMinute + 0.9 * metersPerMinute * grade;
        return vo2 / RestingVo2;
    }

    private static int LevelOf(double value, double[] bounds)
    {
        var level = 1;
        while (level <= bounds.Length && value >= bounds[level - 1])
            level++;
        return level;
    }
}
