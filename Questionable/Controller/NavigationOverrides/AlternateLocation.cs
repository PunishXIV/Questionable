using System.Globalization;
using System.Numerics;

namespace Questionable.Controller.NavigationOverrides;

public sealed record AlternateLocation(Vector3 Point, bool RecalculateNavmesh)
{
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"{nameof(Point)}: {Point:G}, {nameof(RecalculateNavmesh)}: {RecalculateNavmesh}");
    }
}
