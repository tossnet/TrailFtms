namespace TrailFtms.Services.Bluetooth;

/// <summary>User ceilings applied to every target before it is sent. Null: machine limit only.</summary>
public sealed record TreadmillLimits(double? MaxSpeed = null, double? MaxIncline = null);
