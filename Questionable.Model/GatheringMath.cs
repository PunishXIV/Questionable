using System;
using System.Numerics;
using Questionable.Model.Gathering;

namespace Questionable.Model;

public static class GatheringMath
{
    private static readonly Random Rng = new Random();

    public static (Vector3, int, float) CalculateLandingLocation(GatheringLocation location)
    {
        int degrees;
        if (location.IsCone())
            degrees = Rng.Next(
                location.MinimumAngle.GetValueOrDefault(),
                location.MaximumAngle.GetValueOrDefault());
        else
            degrees = Rng.Next(0, 360);

        float range = Rng.Next(
                          (int)(location.CalculateMinimumDistance() * 100),
                          (int)(location.CalculateMaximumDistance() * 100))
                      / 100f;
        return (CalculateLandingLocation(location.Position, degrees, range), degrees, range);
    }

    public static Vector3 CalculateLandingLocation(GatheringLocation location, float angleScale, float rangeScale)
    {
        int degrees;
        if (location.IsCone())
            degrees = location.MinimumAngle.GetValueOrDefault()
                      + (int)(angleScale * (location.MaximumAngle.GetValueOrDefault() -
                                            location.MinimumAngle.GetValueOrDefault()));
        else
            degrees = (int)(rangeScale * 360);

        float range =
            location.CalculateMinimumDistance() +
            rangeScale * (location.CalculateMaximumDistance() - location.CalculateMinimumDistance());
        return CalculateLandingLocation(location.Position, degrees, range);
    }

    private static Vector3 CalculateLandingLocation(Vector3 position, int degrees, float range)
    {
        float rad = -(float)(degrees * Math.PI / 180);
        return new Vector3
        {
            X = position.X + range * (float)Math.Sin(rad),
            Y = position.Y,
            Z = position.Z + range * (float)Math.Cos(rad)
        };
    }
}
