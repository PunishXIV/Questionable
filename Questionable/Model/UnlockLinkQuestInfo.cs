using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LLib.GameData;
using Questionable.Model.Questing;

namespace Questionable.Model;

internal sealed class UnlockLinkQuestInfo : IQuestInfo
{

    public UnlockLinkQuestInfo(UnlockLinkId unlockLinkId, string name, uint issuerDataId, DateTime? expiryTime, string? patch = null)
    {
        QuestId = unlockLinkId;
        Name = name;
        IssuerDataId = issuerDataId;
        QuestExpiry = expiryTime;
        Patch = patch;
    }

    public ElementId QuestId { get; }
    public string Name { get; }
    public uint IssuerDataId { get; }
    public bool IsRepeatable => false;
    public DateTime? QuestExpiry { get; }
    public string? Patch { get; }
    public ImmutableList<PreviousQuestInfo> PreviousQuests => [];
    public EQuestJoin PreviousQuestJoin => EQuestJoin.All;
    public ushort Level => 1;
    public EAlliedSociety AlliedSociety => EAlliedSociety.None;
    public uint? JournalGenre => null;
    public ushort SortKey => 0;
    public bool IsMainScenarioQuest => false;
    public IReadOnlyList<EClassJob> ClassJobs => [];
    public EExpansionVersion Expansion => EExpansionVersion.ARealmReborn;
}
