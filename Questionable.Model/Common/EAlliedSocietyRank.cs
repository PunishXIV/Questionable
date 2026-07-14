using System.Diagnostics.CodeAnalysis;
namespace Questionable.Model.Common;

[SuppressMessage("Design", "CA1028", Justification = "Game type")]
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
