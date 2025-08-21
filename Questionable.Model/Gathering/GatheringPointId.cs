using System;
using System.Globalization;

namespace Questionable.Model.Gathering;

public class GatheringPointId : IComparable<GatheringPointId>, IEquatable<GatheringPointId>
{
    public GatheringPointId(ushort value)
    {
        Value = value;
    }

    public ushort Value { get; }

    public int CompareTo(GatheringPointId? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (ReferenceEquals(null, other)) return 1;
        return Value.CompareTo(other.Value);
    }

    public bool Equals(GatheringPointId? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((GatheringPointId)obj);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static bool operator ==(GatheringPointId? left, GatheringPointId? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(GatheringPointId? left, GatheringPointId? right)
    {
        return !Equals(left, right);
    }

    public static GatheringPointId FromString(string value)
    {
        return new GatheringPointId(ushort.Parse(value, CultureInfo.InvariantCulture));
    }
}
