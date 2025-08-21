using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace Questionable.Model;

[SuppressMessage("Design", "CA1028", Justification = "Game type")]
[UsedImplicitly(ImplicitUseTargetFlags.Members)]
public enum EAlliedSociety : byte
{
    None = 0,
    Amaljaa = 1,
    Sylphs = 2,
    Kobolds = 3,
    Sahagin = 4,
    Ixal = 5,
    VanuVanu = 6,
    Vath = 7,
    Moogles = 8,
    Kojin = 9,
    Ananta = 10,
    Namazu = 11,
    Pixies = 12,
    Qitari = 13,
    Dwarves = 14,
    Arkasodara = 15,
    Omicrons = 16,
    Loporrits = 17,
    Pelupelu = 18,
    MamoolJa = 19,
}
