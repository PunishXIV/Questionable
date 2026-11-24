using System.Diagnostics.CodeAnalysis;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Addon = Lumina.Excel.Sheets.Addon;
using ContentRoulette = Lumina.Excel.Sheets.ContentRoulette;
using ExVersion = Lumina.Excel.Sheets.ExVersion;
using JournalCategory = Lumina.Excel.Sheets.JournalCategory;
using JournalGenre = Lumina.Excel.Sheets.JournalGenre;
namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class PriorityPresets(
    Configuration configuration,
    QuestData questData,
    QuestRegistry questRegistry,
    QuestFunctions questFunctions)
{
    // Saved ids use these keys; the names shown change with the UI language.
    internal const string BuiltInPrefix = "builtin:";
    internal const string CustomPrefix = "custom:";
    private const string JobQuestsKey = "JobQuests";

    internal enum EGroup
    {
        Story,
        AetherCurrents,
        RoleQuests,
        Experimental,
    }

    // Short is the name under its group heading; Name is shown everywhere else.
    internal sealed record Preset(string Key, string Name, List<ElementId> Quests, EGroup Group = EGroup.Story,
        string? Short = null, string? Hint = null);

    private List<Preset>? _builtIn;

    public static string GroupName(EGroup group) => group switch
    {
        EGroup.AetherCurrents => _T<Addon>(2445),
        EGroup.RoleQuests => _T<JournalCategory>(95),
        EGroup.Experimental => _L("Experimental"),
        _ => _L("Story & unlocks"),
    };

    public IEnumerable<IGrouping<EGroup, Preset>> Grouped(bool debug) =>
        BuiltIn.Where(x => debug || x.Group != EGroup.Experimental).GroupBy(x => x.Group);

    public string JobQuestsName { get; } = _L("Job Quests");

    public IReadOnlyList<Preset> BuiltIn => _builtIn ??= Create();

    public void Reload() => _builtIn = null;

    public bool TryGetByName(string name, [NotNullWhen(true)] out List<ElementId>? quests)
    {
        if (BuiltIn.FirstOrDefault(x => x.Name == name) is { } preset)
        {
            quests = GetQuests(preset);
            return true;
        }

        return TryGetCustom(name, out quests);
    }

    public bool TryGetById(string id, [NotNullWhen(true)] out string? name,
        [NotNullWhen(true)] out List<ElementId>? quests)
    {
        quests = null;
        name = GetName(id);
        if (name == null)
            return false;

        if (id.StartsWith(CustomPrefix, StringComparison.Ordinal))
            return TryGetCustom(name, out quests);

        quests = GetQuests(BuiltIn.First(x => BuiltInPrefix + x.Key == id));
        return true;
    }

    public string? GetName(string id)
    {
        if (id.StartsWith(CustomPrefix, StringComparison.Ordinal))
        {
            string name = id[CustomPrefix.Length..];
            return configuration.Priority.Presets.ContainsKey(name) ? name : null;
        }

        return BuiltIn.FirstOrDefault(x => BuiltInPrefix + x.Key == id)?.Name;
    }

    private bool TryGetCustom(string name, [NotNullWhen(true)] out List<ElementId>? quests)
    {
        quests = null;
        if (!configuration.Priority.Presets.TryGetValue(name, out List<string>? questIds))
            return false;

        quests = [];
        foreach (string s in questIds)
        {
            if (ElementId.TryFromString(s, out ElementId? id) && id != null)
                quests.Add(id);
        }

        return true;
    }

    private List<ElementId> GetQuests(Preset preset) => preset.Key == JobQuestsKey
        ? GetCurrentJobQuests()
        : preset.Quests.Where(q => !questFunctions.IsQuestUnobtainable(q) && !questFunctions.IsQuestComplete(q)).ToList();

    private List<ElementId> GetRoleQuests(Job job) => questData.GetRoleQuests(job).Select(x => x.QuestId).ToList();

    private unsafe List<ElementId> GetCurrentJobQuests()
    {
        Job currentJob = (Job)(PlayerState.Instance()->CurrentClassJobId);
        if (currentJob == Job.ADV)
            return [];

        return questRegistry.GetKnownClassJobQuests(currentJob, includeRoleQuests: false)
            .Select(x => x.QuestId)
            .ToList();
    }

    private List<Preset> Create()
    {
        List<ElementId> gilQuests = ((ushort[])[
            835, 903, 916, 918, 919, 920, 929, 928, 930, 931, 932, 945, 1010, 1011, 1015, 1017, 1019, 1553, 1021, 1023,
            1024, 1025, 1026, 1027, 1028, 1029, 1030, 1031, 1032, 1033, 1034, 1035
        ]).FromNumericListOfQuests();
        List<ElementId> dohDolUnlocks = ((ushort[])[
            152,  // desynth
            639,  // melding
            3018, 1422, // craft glamour prisms (blocks the other)
            2095, // inscrutable
            2096, // specialist
            2097, // reduction
            2098, // collectables lv60
            1551, // zhloe custom delivery
        ]).FromNumericListOfQuests();
        List<ElementId> postArrUnlocks = ((ushort[])[
            // don't add DoH/DoL unlocks to this
            434, 435, 488, 3081, 3201, 437, 489, 436,  // gold saucer
            1212, 565, // unlock LB, then second part of 436 ^, then unlock rest of housing districts
            1213, 1214, // housing districts
            160, 1463, // materia
            699, 3017, 1558, // dyes, glams
            1210, // aesthetician
            1211, // treasure maps
            1431, // challenge log
            1432, 1433, 1434, // retainers
            1563, 1564, 1565, // hunts
            1004, 1005, // pvp
            4644, // island sanc visit
            3759, // new game+
            5187, // free fantasia
            94, // sastacha hard
            697, 1410, // halatali
            764, 96, // qarn
            870, 431, // wanderer's palace
            921, // cutter's cry
            1128, 1129, 1130, // dzemael gc
            1131, 1132, 1133, // aurum gc
            1135, // amdapor (requires aurum)
            430, // amdapor hard
            1208, 1209, // pharos sirius
            1215, // haukke hard
            1216, // copperbell hard
            1389, // lost city of amdapor
            1411, // brayflox hard
            1524, // tamtara hard
            1525, // stone vigil hard
            1526, // hullbreaker isle
            1556, // palace of the dead
            1308, // ultimates
            705, // ARR relics
            1048,1157,1158,1194,1195,1196,1197,1198,1412,1413,1530,90, // primal EX
            1008, 1009, 1012, 433, // urth's fount chain
        ]).FromNumericListOfQuests();
        List<ElementId> postHwUnlocks = ((ushort[])[
            2248, // hullbreaker hard
            2201, // sirius hard
            2282, // lost city amdapor hard
            2203, // thordan ex
            2119, // hw hunts
            2113, // fractal continuum
            2112, // neverreap
            2202, // arboretum
            2118, // stone sky sea
            2392, // WT
            2386, // gubal hard
            2402, // sohm al hard
        ]).FromNumericListOfQuests();
        // Highest unlock level first.
        List<ElementId> jobUnlocks = ((ushort[])[
            4854,4855, // Pictomancer (80)
            4848,4849, // Viper (80)
            4067,4068, // Sage (70)
            4073,4074, // Reaper (70)
            3261,3262, // Gunbreaker (60)
            3249,3250, // Dancer (60)
            5490,5491, // Beastmaster (50)
            3192,3193, // Blue Mage (50)
            2559,2560, // Samurai (50)
            2576,2577, // Red Mage (50)
            2109,1696,1697, // Machinist (50)
            2110,2053,2054, // Dark Knight (50)
            2123,2012,2013, // Astrologian (50)
            181,131,219, // Archer
            180,132,218, // Lancer
            182,133,211, // Conjurer
            184,138,205, // Carpenter
            188,105,209, // Leatherworker
            193,3,208, // Botanist
            179,310,312, // Marauder
            451,452,454, // Arcanist
            101,102,104, // Rogue
            1134,1107,1108, // Fisher
            185,291,292, // Blacksmith
            186,273,274, // Armorer
            191,271,254, // Culinarian
            177,285,286, // Gladiator
            183,344,346, // Thaumaturge
            178,532,553, // Pugilist
            187,608,610, // Goldsmith
            189,534,535, // Weaver
            190,575,577, // Alchemist
            192,597,599, // Miner
        ]).FromNumericListOfQuests();
        List<ElementId> unlockCustomDeliveries = ((ushort[])[
            2095,2097,2098,1551,                                // zhloe aliapoh
            2941,3005,                                          // m'naago
            2632,2704,2705,2706,2707,3139,                      // kurenai
            2758,3177,                                          // adkiragh
            3603,3729,                                          // kai-shirr
            3672,3725,3726,3727,3728,3837,3889,                 // ehll tou
            3955,3956,3957,3958,3960,3999,4000,4001,4002,4079,  // charlemend
            4175,4523,                                          // ameliance
            4715,                                               // anden
            4815,                                               // margrat
            5008,5239,                                          // nitowikwe
            5460,                                               // tiisol ja
        ]).FromNumericListOfQuests();
        List<ElementId> unlockLeves = ((ushort[])[
            693,220,687,    // Initial unlock
            58,59,60,       // hest
            694,695,696,14, // la noscea
            221,443,444,    // shroud
            688,692,15,     // thanalan
            16,17,          // coerthas
            18,             // mor dhona
        ]).FromNumericListOfQuests();
        var aetherCurrents = _T<Addon>(2445);
        var roleQuests = _T<JournalCategory>(95);
        var gilSackQuests = questData.GetQuestsWithItemReward(onlyAvailable: true, 5825, 5826, 27994);
        var healthPercentQuests = questData.GetQuestsMatching(q =>
            questRegistry.TryGetQuest(q.QuestId, out var quest) &&
            quest.AllSteps().Any(a => a.Step.CombatItemUse?.Condition is ECombatItemUseCondition.HealthPercent), onlyAvailable: false);
        return
        [
            new(JobQuestsKey, JobQuestsName, []),
            new("LevelCapDungeons", _T<ContentRoulette>(8), ((ushort[])[4959, 5013, 5014]).FromNumericListOfQuests()),
            new("Gil", _L("Gil"), gilQuests, Hint: _L("Tick 'Allagan pieces' under Quest reward choice.")),
            new("JobUnlocks", _L("Unlock all jobs"), jobUnlocks),
            new("CrafterGathererUnlocks", _L("DoH/DoL unlocks"), dohDolUnlocks),
            new("PostRealmRebornUnlocks", _L("Post-ARR unlocks"), postArrUnlocks),
            new("PostHeavenswardUnlocks", _L("Post-HW unlocks"), postHwUnlocks),
            new("Levequests", _T<Addon>(455), unlockLeves),
            new("DeliveryMoogle", _T<JournalGenre>(94), QuestData.DeliveryMoogleQuests.ToList()),
            new("Primals", _T<JournalCategory>(16), QuestData.HardModePrimals.Cast<ElementId>().ToList()),
            new("CrystalTower", _T<JournalCategory>(18), QuestData.CrystalTowerQuests.Cast<ElementId>().ToList()),
            new("CustomDeliveries", _T<Addon>(5700), unlockCustomDeliveries),
            Currents(1, GetAetherCurrentQuests(397, 398, 399, 400, 401)),
            Currents(2, GetAetherCurrentQuests(612, 613, 614, 620, 621, 622)),
            Currents(3, GetAetherCurrentQuests(813, 814, 815, 816, 817, 818)),
            Currents(4, GetAetherCurrentQuests(956, 957, 958, 959, 960, 961)),
            Currents(5, GetAetherCurrentQuests(1187, 1188, 1189, 1190, 1191, 1192)),
            Role("RoleQuestsTank", 1082, Job.PLD),
            Role("RoleQuestsHealer", 1083, Job.WHM),
            Role("RoleQuestsMelee", 1084, Job.MNK),
            Role("RoleQuestsRanged", 1085, Job.BRD),
            Role("RoleQuestsCaster", 1086, Job.BLM),
            new("GilSacks", "Experimental: Available quests rewarding silver/gold gil sacks", gilSackQuests, EGroup.Experimental),
            new("HealthPercent", "Experimental: Quests that require a mob to be damaged to a certain health%", healthPercentQuests,
                EGroup.Experimental),
        ];

        Preset Currents(uint expansion, List<ElementId> quests)
        {
            string name = _T<ExVersion>(expansion);
            return new($"AetherCurrents{expansion}", $"{aetherCurrents}: {name}", quests, EGroup.AetherCurrents, name);
        }

        Preset Role(string key, uint addon, Job job)
        {
            string name = _T<Addon>(addon);
            return new(key, $"{roleQuests}: {name}", GetRoleQuests(job), EGroup.RoleQuests, name);
        }
    }

    private static List<ElementId> GetAetherCurrentQuests(params uint[] territories)
    {
        return territories
            .SelectMany(t => GetZoneEntry(t).Concat(QuestData.AetherCurrentQuestsByTerritory.GetValueOrDefault(t, [])))
            .ToList();

        // The zone's field currents come right before its current quests.
        static IEnumerable<ElementId> GetZoneEntry(uint territory) =>
            QuestData.AetherCurrentZoneQuests.ContainsKey((ushort)territory) ? [new AetherCurrentId((ushort)territory)] : [];
    }
}
