namespace LLib.Gear;

public sealed record StatInfo(short EquipmentValue, short MateriaValue, bool Overcapped)
{
    public short TotalValue => (short)(EquipmentValue + MateriaValue);
}
