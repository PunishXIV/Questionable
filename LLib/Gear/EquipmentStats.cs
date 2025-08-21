using System;
using System.Collections.Generic;
using System.Linq;

namespace LLib.Gear;

public sealed record EquipmentStats(Dictionary<EBaseParam, StatInfo> Stats, byte MateriaCount)
{
    public short Get(EBaseParam param)
    {
        return (short)(GetEquipment(param) + GetMateria(param));
    }

    public short GetEquipment(EBaseParam param)
    {
        Stats.TryGetValue(param, out StatInfo? v);
        return v?.EquipmentValue ?? 0;
    }

    public short GetMateria(EBaseParam param)
    {
        Stats.TryGetValue(param, out StatInfo? v);
        return v?.MateriaValue ?? 0;
    }

    public bool IsOvercapped(EBaseParam param)
    {
        Stats.TryGetValue(param, out StatInfo? v);
        return v?.Overcapped ?? false;
    }

    public bool Has(EBaseParam substat) => Stats.ContainsKey(substat);
    public bool HasMateria() => Stats.Values.Any(x => x.MateriaValue > 0);

    public bool Equals(EquipmentStats? other)
    {
        return other != null &&
               MateriaCount == other.MateriaCount &&
               Stats.SequenceEqual(other.Stats, new KeyValuePairComparer());
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(MateriaCount, Stats);
    }

    private sealed class KeyValuePairComparer : IEqualityComparer<KeyValuePair<EBaseParam, StatInfo>>
    {
        public bool Equals(KeyValuePair<EBaseParam, StatInfo> x, KeyValuePair<EBaseParam, StatInfo> y)
        {
            return x.Key == y.Key && Equals(x.Value, y.Value);
        }

        public int GetHashCode(KeyValuePair<EBaseParam, StatInfo> obj)
        {
            return HashCode.Combine((int)obj.Key, obj.Value);
        }
    }
}
