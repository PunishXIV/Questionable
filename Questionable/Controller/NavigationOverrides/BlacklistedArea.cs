using System.Numerics;

namespace Questionable.Controller.NavigationOverrides;

internal sealed record BlacklistedArea(
    ushort TerritoryId,
    Vector3 Center,
    float MinDistance,
    float MaxDistance,
    bool RecalculateNavmesh = false) : IBlacklistedLocation
{
    public AlternateLocation? AdjustPoint(Vector3 point)
    {
        float distance = (point - Center).Length();
        if (distance < MinDistance || distance > MaxDistance)
            return null;

        return new AlternateLocation(Center + Vector3.Normalize(point - Center) * MaxDistance, RecalculateNavmesh);
    }
}
