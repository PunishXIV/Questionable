using System;
using System.Globalization;
namespace Questionable.Model.Gathering;

public class GatheringPointId(ushort value) : IComparable<GatheringPointId>, IEquatable<GatheringPointId>
{
    public ushort Value { get; } = value;

    public int CompareTo(GatheringPointId? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (other is null) return 1;
        return Value.CompareTo(other.Value);
    }

    public bool Equals(GatheringPointId? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj)) return true;
        if (obj is null) return false;
        if (obj.GetType() != GetType()) return false;
        return Equals((GatheringPointId)obj);
    }

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(GatheringPointId? left, GatheringPointId? right) => Equals(left, right);

    public static bool operator !=(GatheringPointId? left, GatheringPointId? right) => !Equals(left, right);

    public static GatheringPointId FromString(string value) => new(ushort.Parse(value, CultureInfo.InvariantCulture));
}
