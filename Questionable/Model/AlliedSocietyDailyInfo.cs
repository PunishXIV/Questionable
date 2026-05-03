using ECommons.ExcelServices;
using Lumina.Excel.Sheets;
using Questionable.Data;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
namespace Questionable.Model;

internal sealed class AlliedSocietyDailyInfo(BeastTribe beastTribe, byte rank, ClassJobUtils classJobUtils) : IQuestInfo
{
    public ElementId QuestId { get; } = new AlliedSocietyDailyId((byte)beastTribe.RowId, rank);
    public string Name { get; } = beastTribe.Name.ToString();
    public uint IssuerDataId => 0;
    public ImmutableList<PreviousQuestInfo> PreviousQuests { get; } = [];
    public EQuestJoin PreviousQuestJoin => EQuestJoin.All;
    public bool IsRepeatable => true;
    public ushort Level => 1;
    public EAlliedSociety AlliedSociety => EAlliedSociety.None;
    public uint? JournalGenre => null;
    public ushort SortKey => 0;
    public bool IsMainScenarioQuest => false;
    public IReadOnlyList<Job> ClassJobs { get; } = (EAlliedSociety)beastTribe.RowId switch
    {
        EAlliedSociety.Amaljaa or EAlliedSociety.Sylphs or EAlliedSociety.Kobolds or EAlliedSociety.Sahagin or
            EAlliedSociety.VanuVanu or EAlliedSociety.Vath or
            EAlliedSociety.Kojin or EAlliedSociety.Ananta or
            EAlliedSociety.Pixies or
            EAlliedSociety.Arkasodara or
            EAlliedSociety.Pelupelu =>
            [
                ..classJobUtils.AsIndividualJobs(EExtendedClassJob.DoW, null),
                ..classJobUtils.AsIndividualJobs(EExtendedClassJob.DoM, null)
            ],
        EAlliedSociety.Ixal or
            EAlliedSociety.Moogles or
            EAlliedSociety.Dwarves or
            EAlliedSociety.Loporrits or
            EAlliedSociety.YokHuy =>
            classJobUtils.AsIndividualJobs(EExtendedClassJob.DoH, null).ToList(),

        EAlliedSociety.Qitari or
            EAlliedSociety.Omicrons or
            EAlliedSociety.MamoolJa =>
            classJobUtils.AsIndividualJobs(EExtendedClassJob.DoL, null).ToList(),

        EAlliedSociety.Namazu =>
        [
            ..classJobUtils.AsIndividualJobs(EExtendedClassJob.DoH, null),
            ..classJobUtils.AsIndividualJobs(EExtendedClassJob.DoL, null)
        ],

        var _ => throw new ArgumentOutOfRangeException(nameof(beastTribe))
    };
    public EExpansionVersion Expansion { get; } = (EExpansionVersion)beastTribe.Expansion.RowId;
}
