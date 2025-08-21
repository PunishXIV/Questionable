using System;
using System.Numerics;
using System.Text.Json.Serialization;
using Questionable.Model.Common.Converter;

namespace Questionable.Model.Gathering;

public sealed class GatheringLocation
{
    [JsonIgnore]
    public Guid InternalId { get; } = Guid.NewGuid();

    [JsonConverter(typeof(VectorConverter))]
    public Vector3 Position { get; set; }

    public int? MinimumAngle { get; set; }
    public int? MaximumAngle { get; set; }
    public float? MinimumDistance { get; set; }
    public float? MaximumDistance { get; set; }

    public bool IsCone()
    {
        return MinimumAngle != null && MaximumAngle != null;
    }

    public float CalculateMinimumDistance() => MinimumDistance ?? 1f;
    public float CalculateMaximumDistance() => MaximumDistance ?? 3f;
}
