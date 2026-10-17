namespace TrailFtms.Services.Ride;

public enum Sex
{
    Unspecified,
    Female,
    Male,
}

/// <summary>What the calorie estimate needs to know about the runner. Kept in the browser only.</summary>
public sealed record BodyProfile
{
    public double WeightKg { get; init; } = 70;

    /// <summary>Used with the age, only when the heart rate is known.</summary>
    public Sex Sex { get; init; }

    public int Age { get; init; } = 40;
}
