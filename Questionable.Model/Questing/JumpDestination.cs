using Questionable.Model.Common.Converter;
using System.Numerics;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing;

public sealed class JumpDestination
{
    [JsonConverter(typeof(VectorConverter))]
    public Vector3 Position { get; set; }

    public float? StopDistance { get; set; }
    public float? DelaySeconds { get; set; }
    public EJumpType Type { get; set; } = EJumpType.SingleJump;

    public float CalculateStopDistance() => StopDistance ?? 1f;
}
