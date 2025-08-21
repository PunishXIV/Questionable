using System.Numerics;

namespace Questionable.Controller.NavigationOverrides;

internal interface IBlacklistedLocation
{
    ushort TerritoryId { get; }

    AlternateLocation? AdjustPoint(Vector3 point);
}
