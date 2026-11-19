namespace GatheringPathRenderer.Windows;

internal sealed class LocationOverride
{
    public int? MinimumAngle { get; set; }
    public int? MaximumAngle { get; set; }
    public float? MinimumDistance { get; set; }
    public float? MaximumDistance { get; set; }

    public bool IsCone() => MinimumAngle != null && MaximumAngle != null && MinimumAngle != MaximumAngle;

    public bool NeedsSave() => (MinimumAngle != null && MaximumAngle != null) || (MinimumDistance != null && MaximumDistance != null);
}
