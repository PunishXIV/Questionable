using System;
using System.Globalization;
using System.Numerics;

namespace Questionable.Utils;

internal static class Vector3Extensions
{
    internal static float Length_XZ(this Vector3 value)
    {
        return MathF.Sqrt(value.X * value.X + value.Z * value.Z);
    }

    internal static float DistanceTo_XZ(this Vector3 value1, Vector3 value2)
    {
        Vector3 difference = value1 - value2;
        return MathF.Sqrt(difference.X * difference.X + difference.Z * difference.Z);
    }

    internal static Vector3 AsVector3(this Lumina.Excel.Sheets.Level level)
    {
        return new(level.X, level.Y, level.Z);
    }

    internal static string ToInternalString(this Vector3 vector)
    {
        return $"{vector.X.ToString(CultureInfo.InvariantCulture)} {vector.Y.ToString(CultureInfo.InvariantCulture)} {vector.Z.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static string ToJsonString(this Vector3 vector)
    {
        return $$"""
                 { "X": {{vector.X.ToString(CultureInfo.InvariantCulture)}}, "Y": {{vector.Y.ToString(CultureInfo.InvariantCulture)}}, "Z": {{vector.Z.ToString(CultureInfo.InvariantCulture)}} }
                 """;
    }
}