using System.Diagnostics.CodeAnalysis;

namespace LLib.Gear;

[SuppressMessage("Design", "CA1028", Justification = "Game Value")]
public enum EBaseParam : byte
{
    None = 0,

    Strength = 1,
    Dexterity = 2,
    Vitality = 3,
    Intelligence = 4,
    Mind = 5,
    Piety = 6,

    GP = 10,
    CP = 11,

    DamagePhys = 12,
    DamageMag = 13,

    DefensePhys = 21,
    DefenseMag = 24,

    Tenacity = 19,
    Crit = 27,
    DirectHit = 22,
    Determination = 44,
    SpellSpeed = 46,
    SkillSpeed = 45,

    Craftsmanship = 70,
    Control = 71,
    Gathering = 72,
    Perception = 73,
}
