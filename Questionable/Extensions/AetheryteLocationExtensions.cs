using System.Numerics;
using System.Runtime.CompilerServices;
using Questionable.Data;
using Questionable.Model.Common;

namespace Questionable.Extensions;

internal static class AetheryteLocationExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector3 Position(this EAetheryteLocation aetheryteLocation, AetheryteData aetheryteData) => aetheryteData.Locations[aetheryteLocation];
    internal static uint Territory(this EAetheryteLocation aetheryteLocation, AetheryteData aetheryteData) => aetheryteData.TerritoryIds[aetheryteLocation];
}
