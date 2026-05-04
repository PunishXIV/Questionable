using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Questionable.Model;
using Questionable.Model.Questing;
using Quest = Lumina.Excel.Sheets.Quest;

namespace Questionable.Data;

internal sealed class QuestData
{
    public static readonly IReadOnlyList<QuestId> HardModePrimals = [new(1048), new(1157), new(1158)];

    public static readonly IReadOnlyList<QuestId> CrystalTowerQuests =
        [new(1709), new(1200), new(1201), new(1202), new(1203), new(1474), new(494), new(495)];

    public static readonly ImmutableDictionary<uint, ImmutableList<QuestId>> AetherCurrentQuestsByTerritory =
        new Dictionary<uint, List<ushort>>
            {
                // Heavensward
                { 397, [1744, 1759, 1760, 2111] },
                { 398, [1771, 1790, 1797, 1802] },
                { 399, [1936, 1945, 1963, 1966] },
                { 400, [1819, 1823, 1828, 1835] },
                { 401, [1748, 1874, 1909, 1910] },

                // Stormblood
                { 612, [2639, 2661, 2816, 2821] },
                { 613, [2632, 2673, 2687, 2693] },
                { 614, [2724, 2728, 2730, 2733] },
                { 620, [2655, 2842, 2851, 2860] },
                { 621, [2877, 2880, 2881, 2883] },
                { 622, [2760, 2771, 2782, 2791] },

                // Shadowbringers
                { 813, [3380, 3384, 3385, 3386] },
                { 814, [3360, 3371, 3537, 3556] },
                { 815, [3375, 3503, 3511, 3525] },
                { 816, [3395, 3398, 3404, 3427] },
                { 817, [3444, 3467, 3478, 3656] },
                { 818, [3588, 3592, 3593, 3594] },

                // Endwalker
                { 956, [4320, 4329, 4480, 4484] },
                { 957, [4203, 4257, 4259, 4489] },
                { 958, [4216, 4232, 4498, 4502] },
                { 959, [4240, 4241, 4253, 4516] },
                { 960, [4342, 4346, 4354, 4355] },
                { 961, [4288, 4313, 4507, 4511] },

                // Dawntrail
                { 1187, [5039, 5047, 5051, 5055] },
                { 1188, [5064, 5074, 5081, 5085] },
                { 1189, [5094, 5103, 5110, 5114] },
                { 1190, [5130, 5138, 5140, 5144] },
                { 1191, [5153, 5156, 5159, 5160] },
                { 1192, [5174, 5176, 5178, 5179] }
            }
            .ToImmutableDictionary(x => x.Key, x => x.Value.Select(y => new QuestId(y)).ToImmutableList());

    private static readonly IReadOnlyList<uint> TankRoleQuestChapters = [136, 154, 178];
    private static readonly IReadOnlyList<uint> HealerRoleQuestChapters = [137, 155, 179];
    private static readonly IReadOnlyList<uint> MeleeRoleQuestChapters = [138, 156, 180];
    private static readonly IReadOnlyList<uint> PhysicalRangedRoleQuestChapters = [138, 157, 181];
    private static readonly IReadOnlyList<uint> CasterRoleQuestChapters = [139, 158, 182];

    public static readonly IReadOnlyList<IReadOnlyList<uint>> AllRoleQuestChapters =
    [
        TankRoleQuestChapters,
        HealerRoleQuestChapters,
        MeleeRoleQuestChapters,
        PhysicalRangedRoleQuestChapters,
        CasterRoleQuestChapters
    ];

    public static readonly IReadOnlyList<QuestId> FinalShadowbringersRoleQuests =
        [new(3248), new(3272), new(3278), new(3628)];

    private readonly Dictionary<ElementId, IQuestInfo> _quests;
    private readonly IPluginLog? _pluginLog;

    public QuestData(IDataManager dataManager, ClassJobUtils classJobUtils, IPluginLog? pluginLog = null)
    {
        _pluginLog = pluginLog;
        JournalGenreOverrides journalGenreOverrides = new()
        {
            ARelicRebornQuests = dataManager.GetExcelSheet<Quest>().GetRow(65742).JournalGenre.RowId,
            RadzAtHanSideQuests = dataManager.GetExcelSheet<Quest>().GetRow(69805).JournalGenre.RowId,
            ThavnairSideQuests = dataManager.GetExcelSheet<Quest>().GetRow(70025).JournalGenre.RowId
        };

        Dictionary<uint, uint> questChapters =
            dataManager.GetExcelSheet<QuestChapter>()
                .Where(x => x.RowId > 0 && x.Quest.RowId > 0)
                .ToDictionary(x => x.Quest.RowId, x => x.Redo.RowId);

        Dictionary<uint, byte> startingCities = [];
        for (byte redoChapter = 1; redoChapter <= 3; ++redoChapter)
        {
            QuestRedo questRedo = dataManager.GetExcelSheet<QuestRedo>().GetRow(redoChapter);
            foreach (QuestRedo.QuestRedoParamStruct quest in questRedo.QuestRedoParam.Where(x => x.Quest.IsValid))
                startingCities[quest.Quest.RowId] = redoChapter;
        }

        List<IQuestInfo> quests =
        [
            ..dataManager.GetExcelSheet<Quest>()
                .Where(x => x.RowId > 0)
                .Where(x => x.IssuerLocation.RowId > 0)
                .Select(x => new QuestInfo(x, questChapters.GetValueOrDefault(x.RowId),
                    startingCities.GetValueOrDefault(x.RowId), journalGenreOverrides)),
            ..dataManager.GetExcelSheet<SatisfactionNpc>()
                .Where(x => x is { RowId: > 0, Npc.RowId: > 0 })
                .Select(x => new SatisfactionSupplyInfo(x))
        ];

        quests.AddRange(
            dataManager.GetExcelSheet<BeastTribe>()
                .Where(x => x.RowId > 0 && !x.Name.IsEmpty)
                .SelectMany(x =>
                {
                    if (x.RowId < 5)
                    {
                        return ((IEnumerable<byte>)
                            [
                                0,
                                ..quests.Where(y => y.AlliedSociety == (EAlliedSociety)x.RowId && y.IsRepeatable)
                                    .Cast<QuestInfo>()
                                    .Select(y => (byte)y.AlliedSocietyRank).Distinct()
                            ])
                            .Select(rank => new AlliedSocietyDailyInfo(x, rank, classJobUtils));
                    }
                    else
                        return [new(x, 0, classJobUtils)];
                }));

        quests.Add(new UnlockLinkQuestInfo(new(506), "Patch 7.2 Fantasia", 1052475));
        quests.Add(new UnlockLinkQuestInfo(new(568), "Patch 7.3 Fantasia", 1052475));

        _quests = quests.ToDictionary(x => x.QuestId, x => x);

        // workaround because the game doesn't require completion of the CT questline through normal means
        AddPreviousQuest(new(425), new(495));

        // "In order to undertake this quest" [...]
        const int mountaintopDiplomacy = 1619;
        const int inscrutableTastes = 2095;
        const int tideGoesIn = 2490;
        const int firstOfMany = 2534;
        const int achtIaOrmhInn = 3320;
        AddPreviousQuest(new(1480), new(2373));
        AddPreviousQuest(new(1717), new(mountaintopDiplomacy));
        AddPreviousQuest(new(2088), new(mountaintopDiplomacy));
        AddPreviousQuest(new(2062), new(1617));
        AddPreviousQuest(new(2063), new(mountaintopDiplomacy));
        AddPreviousQuest(new(2257), new(1655));
        AddPreviousQuest(new(2608), new(firstOfMany));
        AddPreviousQuest(new(2600), new(2466));
        AddPreviousQuest(new(2622), new(tideGoesIn));
        AddPreviousQuest(new(2624), new(firstOfMany));
        AddPreviousQuest(new(2898), new(tideGoesIn));
        AddPreviousQuest(new(2974), new(2491));
        AddPreviousQuest(new(2975), new(2630));
        AddPreviousQuest(new(2912), new(tideGoesIn));
        AddPreviousQuest(new(2914), new(2537));
        AddPreviousQuest(new(2919), new(2455));
        AddPreviousQuest(new(2952), new(2518));
        AddPreviousQuest(new(2904), new(2503));
        AddPreviousQuest(new(3038), new(firstOfMany));
        AddPreviousQuest(new(3087), new(100));
        AddPreviousQuest(new(3246), new(3314));
        AddPreviousQuest(new(3247), new(achtIaOrmhInn));
        AddPreviousQuest(new(3270), new(3333));
        AddPreviousQuest(new(3271), new(3634));
        AddPreviousQuest(new(3264), new(2247));
        AddPreviousQuest(new(3253), new(2247));
        AddPreviousQuest(new(3254), new(2537));
        AddPreviousQuest(new(3228), new(achtIaOrmhInn));
        AddPreviousQuest(new(3234), new(achtIaOrmhInn));
        AddPreviousQuest(new(3237), new(achtIaOrmhInn));
        AddPreviousQuest(new(3238), new(3634));
        AddPreviousQuest(new(3240), new(achtIaOrmhInn));
        AddPreviousQuest(new(3241), new(3648));
        AddPreviousQuest(new(3628), new(3301));
        AddPreviousQuest(new(3655), new(inscrutableTastes));
        AddPreviousQuest(new(3771), new(495));
        AddPreviousQuest(new(4068), new(1658));
        AddPreviousQuest(new(4078), new(1583));
        AddPreviousQuest(new(4150), new(4417));
        AddPreviousQuest(new(4155), new(4383));
        AddPreviousQuest(new(4156), new(3326));
        AddPreviousQuest(new(4158), new(4434));
        AddPreviousQuest(new(4159), new(4464));
        AddPreviousQuest(new(4163), new(4398));
        AddPreviousQuest(new(4165), new(4438));
        AddPreviousQuest(new(4473), new(inscrutableTastes));
        AddPreviousQuest(new(4650), new(2374));
        AddPreviousQuest(new(4662), new(3166));
        AddPreviousQuest(new(4761), new(4032));
        AddPreviousQuest(new(4812), new(4750));
        AddPreviousQuest(new(4851), new(2446));
        AddPreviousQuest(new(4856), new(1669));
        AddPreviousQuest(new(4857), new(2553));
        AddPreviousQuest(new(4979), new(4896));
        AddPreviousQuest(new(4980), new(4911));
        AddPreviousQuest(new(4985), new(4903));
        AddPreviousQuest(new(4987), new(4912));
        AddPreviousQuest(new(4988), new(4942));
        AddPreviousQuest(new(4992), new(4912));
        AddPreviousQuest(new(4999), new(4908));
        AddPreviousQuest(new(4966), new(inscrutableTastes));
        AddPreviousQuest(new(5000), new(4908));
        AddPreviousQuest(new(5001), new(4912));

        // "In order to proceed with this quest" [...]
        /* my little chocobo
        AddPreviousQuest(new QuestId(1036), new QuestId());
        AddPreviousQuest(new QuestId(1663), new QuestId());
        AddPreviousQuest(new QuestId(3771), new QuestId());
        AddPreviousQuest(new QuestId(4521), new QuestId());
        */
        /* only applicable for fishers
        const int spearfishing = 2922;
        AddPreviousQuest(new QuestId(3811), new QuestId(spearfishing));
        AddPreviousQuest(new QuestId(3812), new QuestId(spearfishing));
        AddPreviousQuest(new QuestId(3817), new QuestId(spearfishing));
        AddPreviousQuest(new QuestId(3818), new QuestId(spearfishing));
        AddPreviousQuest(new QuestId(3821), new QuestId(spearfishing));
        AddPreviousQuest(new QuestId(3833), new QuestId(spearfishing));
        */

        // Shadow Walk with Me
        AddPreviousQuest(new(3629), new(3248));
        AddPreviousQuest(new(3629), new(3272));
        AddPreviousQuest(new(3629), new(3278));
        AddPreviousQuest(new(3629), new(3628));

        // The Hero's Journey
        AddPreviousQuest(new(3986), new(2115));
        AddPreviousQuest(new(3986), new(2116));
        AddPreviousQuest(new(3986), new(2281));
        AddPreviousQuest(new(3986), new(2333));
        AddPreviousQuest(new(3986), new(2395));
        AddPreviousQuest(new(3986), new(3985));

        // Picking up the Torch has half the quests in the sheets(??)
        AddPreviousQuest(new(5188), new(4841));
        AddPreviousQuest(new(5188), new(4847));
        AddPreviousQuest(new(5188), new(4959));

        // initial city quests are side quests
        // unclear if 470 can be started as the required quest isn't available anymore
        ushort[] limsaSideQuests =
            [107, 111, 112, 122, 663, 475, 472, 476, 470, 473, 474, 477, 486, 478, 479, 59, 400, 401, 693, 405];
        foreach (ushort questId in limsaSideQuests)
            ((QuestInfo)_quests[new QuestId(questId)]).StartingCity = 1;

        ushort[] gridaniaQuests =
            [39, 1, 32, 34, 37, 172, 127, 130, 60, 220, 378];
        foreach (ushort questId in gridaniaQuests)
            ((QuestInfo)_quests[new QuestId(questId)]).StartingCity = 2;

        ushort[] uldahSideQuests =
            [594, 389, 390, 321, 304, 322, 388, 308, 326, 58, 687, 341, 504, 531, 506, 530, 573, 342, 505];
        foreach (ushort questId in uldahSideQuests)
            ((QuestInfo)_quests[new QuestId(questId)]).StartingCity = 3;

        // follow-up quests to picking a GC
        AddGcFollowUpQuests();

        MainScenarioQuests = _quests.Values.Where(x => x is QuestInfo { IsMainScenarioQuest: true })
            .Cast<QuestInfo>()
            .ToList();

        LastMainScenarioQuestId = MainScenarioQuests
            .Where(x => !MainScenarioQuests.Any(y => y.PreviousQuests.Any(z => z.QuestId == x.QuestId)))
            .Select(x => (QuestId)x.QuestId)
            .FirstOrDefault() ?? new QuestId(0);
        RedeemableItems = quests.Where(x => x is QuestInfo)
            .Cast<QuestInfo>()
            .SelectMany(x => x.ItemRewards)
            .ToImmutableHashSet();
    }

    public static ImmutableHashSet<QuestId> AetherCurrentQuests { get; } =
        AetherCurrentQuestsByTerritory.Values.SelectMany(x => x).ToImmutableHashSet();

    public IReadOnlyList<QuestInfo> MainScenarioQuests { get; }
    public ImmutableHashSet<ItemReward> RedeemableItems { get; }
    public QuestId LastMainScenarioQuestId { get; }

    private void AddPreviousQuest(QuestId questToUpdate, QuestId requiredQuestId)
    {
        if (_quests.TryGetValue(questToUpdate, out IQuestInfo? quest) && quest is QuestInfo questInfo)
            questInfo.AddPreviousQuest(new(requiredQuestId));
    }

    private void AddGcFollowUpQuests()
    {
        QuestId[] questIds = [new(683), new(684), new(685)];
        foreach (QuestId questId in questIds)
        {
            QuestInfo quest = (QuestInfo)_quests[questId];
            quest.AddQuestLocks(EQuestJoin.AtLeastOne, questIds.Where(x => x != questId).ToArray());
        }
    }

    public IQuestInfo GetQuestInfo(ElementId elementId)
    {
        return _quests[elementId] ?? throw new ArgumentOutOfRangeException(nameof(elementId));
    }

    public bool TryGetQuestInfo(ElementId elementId, [NotNullWhen(true)] out IQuestInfo? questInfo)
    {
        return _quests.TryGetValue(elementId, out questInfo);
    }

    public List<IQuestInfo> GetAllByIssuerDataId(uint targetId)
    {
        return _quests.Values
            .Where(x => x.IssuerDataId == targetId)
            .ToList();
    }

    public bool IsIssuerOfAnyQuest(uint targetId)
    {
        return _quests.Values.Any(x => x.IssuerDataId == targetId);
    }

    public List<IQuestInfo> GetAllByJournalGenre(uint journalGenre)
    {
        return _quests.Values
            .Where(x => x is QuestInfo { IsSeasonalEvent: false } or not QuestInfo)
            .Where(x => x.JournalGenre == journalGenre)
            .OrderBy(x => x.SortKey)
            .ThenBy(x => x.QuestId)
            .ToList();
    }

    public List<QuestInfo> GetAllByAlliedSociety(EAlliedSociety alliedSociety)
    {
        return _quests.Values
            .Where(x => x is QuestInfo)
            .Cast<QuestInfo>()
            .Where(x => x.AlliedSociety == alliedSociety)
            .OrderBy(x => x.QuestId)
            .ToList();
    }

    public List<QuestInfo> GetClassJobQuests(Job classJob, bool includeRoleQuests = false)
    {
        List<uint> chapterIds = classJob switch
        {
            Job.ADV => [],
            // ARR
            Job.GLA => [63],
            Job.PLD => [72, 73, 74],
            Job.MRD => [64],
            Job.WAR => [76, 77, 78],
            Job.CNJ => [65],
            Job.WHM => [86, 87, 88],
            Job.ACN => [66],
            Job.SMN => [127, 128, 129],
            Job.SCH => [90, 91, 92],
            Job.PGL => [67],
            Job.MNK => [98, 99, 100],
            Job.LNC => [68],
            Job.DRG => [102, 103, 104],
            Job.ROG => [69],
            Job.NIN => [106, 107, 108],
            Job.ARC => [70],
            Job.BRD => [113, 114, 115],
            Job.THM => [71],
            Job.BLM => [123, 124, 125],
            // HW
            Job.DRK => [80, 81, 82],
            Job.AST => [94, 95, 96],
            Job.MCH => [117, 118, 119],
            // SB
            Job.SAM => [110, 111],
            Job.RDM => [131, 132],
            Job.BLU => [134, 135, 146, 170],
            // ShB
            Job.GNB => [84],
            Job.DNC => [121],
            // EW
            Job.SGE => [152],
            Job.RPR => [153],
            // DT
            Job.VPR => [176],
            Job.PCT => [177],
            // Crafter
            Job.ALC => [48, 49, 50],
            Job.ARM => [36, 37, 38],
            Job.BSM => [33, 34, 35],
            Job.CRP => [30, 31, 32],
            Job.CUL => [51, 52, 53],
            Job.GSM => [39, 40, 41],
            Job.LTW => [42, 43, 44],
            Job.WVR => [45, 46, 47],
            // Gatherer
            Job.MIN => [54, 55, 56],
            Job.BTN => [57, 58, 59],
            Job.FSH => [60, 61, 62],
            var _ => LogUnsupportedClassJobAndReturnEmpty(classJob)
        };

        if (includeRoleQuests)
            chapterIds.AddRange(GetRoleQuestIds(classJob));

        return GetQuestsInNewGamePlusChapters(chapterIds);
    }

    private List<uint> LogUnsupportedClassJobAndReturnEmpty(Job classJob)
    {
        _pluginLog?.Debug("Ignoring unsupported class job in GetClassJobQuests: {ClassJob}", classJob);
        return [];
    }

    public List<QuestInfo> GetRoleQuests(Job referenceClassJob)
    {
        return GetQuestsInNewGamePlusChapters(GetRoleQuestIds(referenceClassJob).ToList());
    }

    private static IEnumerable<uint> GetRoleQuestIds(Job classJob)
    {
        return classJob switch
        {
            var _ when classJob.IsTank() => TankRoleQuestChapters,
            var _ when classJob.IsHealer() => HealerRoleQuestChapters,
            var _ when classJob.IsMelee() => MeleeRoleQuestChapters,
            var _ when classJob.IsPhysicalRanged() => PhysicalRangedRoleQuestChapters,
            var _ when classJob.IsCaster() && classJob != Job.BLU => CasterRoleQuestChapters,
            var _ => []
        };
    }

    private List<QuestInfo> GetQuestsInNewGamePlusChapters(List<uint> chapterIds)
    {
        return _quests.Values
            .Where(x => x is QuestInfo)
            .Cast<QuestInfo>()
            .Where(x => chapterIds.Contains(x.NewGamePlusChapter))
            .ToList();
    }

    public List<QuestId> GetLockedClassQuests()
    {
        Job startingClass;
        unsafe
        {
            PlayerState* playerState = PlayerState.Instance();
            if (playerState != null)
                startingClass = (Job)playerState->FirstClass;
            else
                startingClass = Job.ADV;
        }

        if (startingClass == Job.ADV)
            return [];

        // If you start the game as another class, you get:
        // - "So you want to be a XX"
        // - "Way of the XX" (depends on "So you want to be a XX")
        // - "My First XX"
        // If you start the game with this class, you get:
        // - "Way of the XX" (no preconditions)
        // In both cases, the level 10 quests are different
        List<List<ushort>> startingClassQuests =
        [
            startingClass == Job.GLA ? [177, 285, 286, 288] : [253, 261],
            startingClass == Job.PGL ? [178, 532, 553, 698] : [533, 555],
            startingClass == Job.MRD ? [179, 310, 312, 315] : [311, 314],
            startingClass == Job.LNC ? [180, 132, 218, 143] : [23, 35],
            startingClass == Job.ARC ? [181, 131, 219, 134] : [21, 67],
            startingClass == Job.CNJ ? [182, 133, 211, 147] : [22, 91],
            startingClass == Job.THM ? [183, 344, 346, 349] : [345, 348],
            startingClass == Job.ACN ? [451, 452, 454, 457] : [453, 456]
        ];
        return startingClassQuests.SelectMany(x => x).Select(x => new QuestId(x)).ToList();
    }
}
