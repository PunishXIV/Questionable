using System.Numerics;

namespace Questionable.Controller.NavigationOverrides;

public sealed record BlacklistedPoint(
    ushort TerritoryId,
    Vector3 Original,
    Vector3 Replacement,
    float CheckDistance = 0.05f,
    bool RecalculateNavmesh = false) : IBlacklistedLocation
{
    public AlternateLocation? AdjustPoint(Vector3 point)
    {
        float distance = (point - Original).Length();
        if (distance > CheckDistance)
            return null;

        return new AlternateLocation(Replacement, RecalculateNavmesh);
    }
}
