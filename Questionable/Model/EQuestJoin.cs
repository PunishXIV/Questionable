using JetBrains.Annotations;
using System.Diagnostics.CodeAnalysis;

namespace Questionable.Model;

[SuppressMessage("Design", "CA1028", Justification = "Game type")]
[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
internal enum EQuestJoin : byte
{
    None = 0,
    All = 1,
    AtLeastOne = 2,
}
