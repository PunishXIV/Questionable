using System.Collections.Immutable;
using ECommons.ExcelServices;
using Questionable.Model.Common;
using Questionable.Model.Questing;
namespace Questionable.Domain;

// A zone's field aether currents, run from the priority list like a quest.
internal sealed class AetherCurrentZoneInfo(
    AetherCurrentId zoneId,
    string name,
    EExpansionVersion expansion,
    IReadOnlyList<uint> currentIds,
    ImmutableList<PreviousQuestInfo> previousQuests) : IQuestInfo
{
    public ElementId QuestId { get; } = zoneId;
    public string Name { get; } = name;
    public uint IssuerDataId => 0;
    public bool IsRepeatable => false;
    public ImmutableList<PreviousQuestInfo> PreviousQuests { get; } = previousQuests;
    public EQuestJoin PreviousQuestJoin => EQuestJoin.All;
    public ushort Level => 1;
    public EAlliedSociety AlliedSociety => EAlliedSociety.None;
    public uint? JournalGenre => null;
    public ushort SortKey => zoneId.Value;
    public bool IsMainScenarioQuest => false;
    public IReadOnlyList<Job> ClassJobs => [];
    public EExpansionVersion Expansion { get; } = expansion;
    public IReadOnlyList<uint> CurrentIds { get; } = currentIds;
}
