using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LLib.GameData;
using Lumina.Excel.Sheets;
using Questionable.Data;
using Questionable.Model.Questing;

namespace Questionable.Model;

internal sealed class AlliedSocietyDailyInfo : IQuestInfo
{
    public AlliedSocietyDailyInfo(BeastTribe beastTribe, byte rank, ClassJobUtils classJobUtils)
    {
        QuestId = new AlliedSocietyDailyId((byte)beastTribe.RowId, rank);
        Name = beastTribe.Name.ToString();
        ClassJobs = (EAlliedSociety)beastTribe.RowId switch
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
                EAlliedSociety.Loporrits =>
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

            _ => throw new ArgumentOutOfRangeException(nameof(beastTribe))
        };
        Expansion = (EExpansionVersion)beastTribe.Expansion.RowId;
    }

    public ElementId QuestId { get; }
    public string Name { get; }
    public uint IssuerDataId => 0;
    public ImmutableList<PreviousQuestInfo> PreviousQuests { get; } = [];
    public EQuestJoin PreviousQuestJoin => EQuestJoin.All;
    public bool IsRepeatable => true;
    public ushort Level => 1;
    public EAlliedSociety AlliedSociety => EAlliedSociety.None;
    public uint? JournalGenre => null;
    public ushort SortKey => 0;
    public bool IsMainScenarioQuest => false;
    public IReadOnlyList<EClassJob> ClassJobs { get; }
    public EExpansionVersion Expansion { get; }
}
