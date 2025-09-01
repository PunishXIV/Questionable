﻿using Questionable.Model.Common.Converter;
using System.Numerics;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing;

public sealed class NearPositionCondition
{
    [JsonConverter(typeof(VectorConverter))]
    public Vector3 Position { get; set; }
    public float MaximumDistance { get; set; }
    public ushort TerritoryId { get; set; }
}
