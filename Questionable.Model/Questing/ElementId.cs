using System;
using System.Globalization;

namespace Questionable.Model.Questing;

public abstract class ElementId : IComparable<ElementId>, IEquatable<ElementId>
{
    protected ElementId(ushort value)
    {
        Value = value;
    }

    public ushort Value { get; }

    public int CompareTo(ElementId? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (ReferenceEquals(null, other)) return 1;
        return Value.CompareTo(other.Value);
    }

    public bool Equals(ElementId? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        if (other.GetType() != GetType()) return false;
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((ElementId)obj);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static bool operator ==(ElementId? left, ElementId? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(ElementId? left, ElementId? right)
    {
        return !Equals(left, right);
    }

    public static ElementId FromString(string value)
    {
        if (value.StartsWith("S"))
            return new SatisfactionSupplyNpcId(ushort.Parse(value.Substring(1), CultureInfo.InvariantCulture));
        else if (value.StartsWith("U"))
            return new UnlockLinkId(ushort.Parse(value.Substring(1), CultureInfo.InvariantCulture));
        else if (value.StartsWith("A"))
        {
            value = value.Substring(1);
            string[] parts = value.Split('x');
            if (parts.Length == 2)
            {
                return new AlliedSocietyDailyId(
                    byte.Parse(parts[0], CultureInfo.InvariantCulture),
                    byte.Parse(parts[1], CultureInfo.InvariantCulture));
            }
            else
                return new AlliedSocietyDailyId(byte.Parse(value, CultureInfo.InvariantCulture));
        }
        else
            return new QuestId(ushort.Parse(value, CultureInfo.InvariantCulture));
    }

    public static bool TryFromString(string value, out ElementId? elementId)
    {
        try
        {
            elementId = FromString(value);
            return true;
        }
        catch (Exception)
        {
            elementId = null;
            return false;
        }
    }

    public abstract override string ToString();
}

public sealed class QuestId(ushort value) : ElementId(value)
{
    public static QuestId FromRowId(uint rowId) => new((ushort)(rowId & 0xFFFF));

    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }
}

public sealed class SatisfactionSupplyNpcId(ushort value) : ElementId(value)
{
    public override string ToString()
    {
        return "S" + Value.ToString(CultureInfo.InvariantCulture);
    }
}

public sealed class UnlockLinkId(ushort value) : ElementId(value)
{
    public override string ToString()
    {
        return "U" + Value.ToString(CultureInfo.InvariantCulture);
    }
}

public sealed class AlliedSocietyDailyId(byte alliedSociety, byte rank = 0) : ElementId((ushort)(alliedSociety * 10 + rank))
{
    public byte AlliedSociety { get; } = alliedSociety;
    public byte Rank { get; } = rank;

    public override string ToString()
    {
        return "A" + AlliedSociety + "x" + Rank;
    }
}
