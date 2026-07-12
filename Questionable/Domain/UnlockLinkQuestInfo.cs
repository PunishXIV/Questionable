using System.Collections.Generic;
using System.Collections.Immutable;
using ECommons.ExcelServices;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
namespace Questionable.Domain;

internal sealed class UnlockLinkQuestInfo(UnlockLinkId unlockLinkId, string name, uint issuerDataId) : IQuestInfo
{
    public ElementId QuestId { get; } = unlockLinkId;
    public string Name { get; } = name;
    public uint IssuerDataId { get; } = issuerDataId;
    public bool IsRepeatable => false;
    public ImmutableList<PreviousQuestInfo> PreviousQuests => [];
    public EQuestJoin PreviousQuestJoin => EQuestJoin.All;
    public ushort Level => 1;
    public EAlliedSociety AlliedSociety => EAlliedSociety.None;
    public uint? JournalGenre => null;
    public ushort SortKey => 0;
    public bool IsMainScenarioQuest => false;
    public IReadOnlyList<Job> ClassJobs => [];
    public EExpansionVersion Expansion => EExpansionVersion.ARealmReborn;
}
