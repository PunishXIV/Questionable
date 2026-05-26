using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
namespace Questionable.Model;

[SuppressMessage("Design", "CA1028", Justification = "Game type")]
[UsedImplicitly(ImplicitUseTargetFlags.Members)]
public enum EAlliedSocietyRank : byte
{
    None = 0,
    Neutral = 1,
    Recognized = 2,
    Friendly = 3,
    Trusted = 4,
    Respected = 5,
    Honored = 6,
    Sworn = 7,
    Allied = 8
}
