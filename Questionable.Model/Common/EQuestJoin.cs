using System.Diagnostics.CodeAnalysis;
namespace Questionable.Model.Common;

[SuppressMessage("Design", "CA1028", Justification = "Game type")]
public enum EQuestJoin : byte
{
    None = 0,
    All = 1,
    AtLeastOne = 2
}
