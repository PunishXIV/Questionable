using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using NSubstitute;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestFunctionsTests
{
    private readonly FakeQuestGameState _state = new();
    private readonly FakeQuestCatalog _catalog = new();
    private readonly QuestFunctions _functions;

    public QuestFunctionsTests()
    {
        _functions = new QuestFunctions(_catalog, _state, new AlliedSocietyData(), new Configuration(), Substitute.For<IChatGui>());
    }

    private static QuestInfo Daily(ushort id, EAlliedSociety society) => TestQuestInfo.Create(id)
        .With(nameof(QuestInfo.AlliedSociety), society)
        .With(nameof(QuestInfo.IsRepeatable), true);

    private string[] LockReasons(ushort questId) => _functions.IsQuestLocked(new QuestId(questId)).reasons!;

    [Fact]
    public void PreviousQuests_AllNeededMissingNamed()
    {
        _catalog.Add(TestQuestInfo.Create(1, "Wise Guides"));
        _catalog.Add(TestQuestInfo.Create(2, "Sage Council"));
        _catalog.Add(TestQuestInfo.Create(3, previousJoin: EQuestJoin.All, previous: [new(new QuestId(1)), new(new QuestId(2))]));

        Assert.Equal(["Needs quests: Wise Guides, Sage Council"], LockReasons(3));
        _state.Complete.Add(1);
        Assert.Equal(["Needs quest: Sage Council"], LockReasons(3));
        _state.Complete.Add(2);
        Assert.Empty(LockReasons(3));
    }

    [Fact]
    public void AnyOnePreviousQuestIsEnough()
    {
        _catalog.Add(TestQuestInfo.Create(1, "A"));
        _catalog.Add(TestQuestInfo.Create(2, "B"));
        _catalog.Add(TestQuestInfo.Create(3, previousJoin: EQuestJoin.AtLeastOne, previous: [new(new QuestId(1)), new(new QuestId(2))]));

        Assert.Equal(["Needs one of: A, B"], LockReasons(3));
        _state.Complete.Add(2);
        Assert.Empty(LockReasons(3));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void InProgressPrevious_CountsAtSequence(int sequence, bool locked)
    {
        _catalog.Add(TestQuestInfo.Create(1, "A"));
        _catalog.Add(TestQuestInfo.Create(2, previous: [new(new QuestId(1), Sequence: 3)]));
        _state.Accepted.Add(1);
        _state.Sequences[1] = (byte)sequence;

        Assert.Equal(locked, _functions.IsQuestLocked(new QuestId(2)).isLocked);
    }

    [Fact]
    public void ExtraCompletedQuestUnlocksItsFollowUp()
    {
        _catalog.Add(TestQuestInfo.Create(1, "A"));
        _catalog.Add(TestQuestInfo.Create(2, previous: [new(new QuestId(1))]));

        Assert.False(_functions.IsQuestLocked(new QuestId(2), extraCompletedQuest: new QuestId(1)).isLocked);
    }

    [Fact]
    public void RetainerQuest_RequiresRetainers()
    {
        _catalog.Add(TestQuestInfo.Create(1432));
        _state.HasRetainers = false;

        Assert.Equal(["Retainers"], LockReasons(1432));
    }

    [Fact]
    public void SpecialPrerequisite_ReasonFromTable()
    {
        _catalog.Add(TestQuestInfo.Create(576));
        _state.ChocoboRacingRank = 39;

        Assert.Equal(["Needs chocobo racing rank 40"], LockReasons(576));
        _state.ChocoboRacingRank = 40;
        Assert.Empty(LockReasons(576));
    }

    [Fact]
    public void LockedAetheryte_LocksUnlessAttuned()
    {
        var step = new QuestStep { TerritoryId = 956 };
        _catalog.Add(TestQuestInfo.Create(1), QuestTestData.Seq(0, step));
        _catalog.Add(TestQuestInfo.Create(2), QuestTestData.Seq(0,
            new QuestStep { InteractionType = EInteractionType.AttuneAetheryte, Aetheryte = EAetheryteLocation.LabyrinthosAporia },
            step));
        _state.Aetherytes[956] = [EAetheryteLocation.LabyrinthosAporia];
        _state.LockedAetherytes.Add(EAetheryteLocation.LabyrinthosAporia);

        Assert.Equal(["Aetheryte locked: LabyrinthosAporia"], LockReasons(1));
        Assert.Empty(LockReasons(2));
    }

    [Fact]
    public void LockedAetheryte_WalkInRegionIsFine()
    {
        _catalog.Add(TestQuestInfo.Create(1), QuestTestData.Seq(0, new QuestStep { TerritoryId = 134 }));
        _state.TerritoryType = 128;
        _state.Regions[128] = _state.Regions[134] = 22;
        _state.Aetherytes[134] = [EAetheryteLocation.MiddleLaNosceaSummerfordFarms];
        _state.LockedAetherytes.Add(EAetheryteLocation.MiddleLaNosceaSummerfordFarms);

        Assert.Empty(LockReasons(1));
    }

    private static Quest Travels(params QuestStep[] steps) =>
        QuestTestData.CreateQuest(new QuestId(1), QuestTestData.Seq(0, steps));

    [Fact]
    public void WithoutTeleport_OnlyWithinRegion()
    {
        _state.TerritoryType = 132;
        _state.Regions[132] = _state.Regions[133] = _state.Regions[148] = 23;
        QuestStep[] shroud = [new() { TerritoryId = 132 }, new() { TerritoryId = 133 }, new() { TerritoryId = 148 }];

        Assert.True(_functions.CanBeDoneWithoutTeleport(Travels(shroud)));
        Assert.False(_functions.CanBeDoneWithoutTeleport(Travels([.. shroud, new() { TerritoryId = 130 }])));
    }

    // Way of the Marauder starts in Upper Decks while a new character is still in the opening zone.
    [Fact]
    public void WithoutTeleport_StartsHere()
    {
        _state.TerritoryType = 181;
        _state.Regions[181] = _state.Regions[128] = 22;

        Assert.False(_functions.CanBeDoneWithoutTeleport(Travels(new QuestStep { TerritoryId = 128 })));
    }

    [Fact]
    public void TeleportCost_CheapestPerZoneChange()
    {
        _state.TerritoryType = 132;
        _state.Aetherytes[140] = [EAetheryteLocation.WesternThanalanHorizon];
        _state.Aetherytes[130] = [EAetheryteLocation.Uldah];
        _state.Aetherytes[133] = [EAetheryteLocation.Gridania];
        _state.TeleportCosts = new Dictionary<uint, uint>
        {
            [(uint)EAetheryteLocation.WesternThanalanHorizon] = 300,
            [(uint)EAetheryteLocation.Uldah] = 200,
        };

        // 132 -> 133 and 130 -> 131 are city halves.
        Assert.Equal(500, _functions.GetTeleportCost(Travels(
            new() { TerritoryId = 133 }, new() { TerritoryId = 140 }, new() { TerritoryId = 130 }, new() { TerritoryId = 131 })));
        // A door step already lands in 130.
        Assert.Equal(300, _functions.GetTeleportCost(Travels(
            new() { TerritoryId = 140, TargetTerritoryId = 130 }, new() { TerritoryId = 130 })));
    }

    [Theory]
    [InlineData(2, 100, 100, false)]
    [InlineData(2, 99, 100, true)]
    [InlineData(3, 0, 100, false)]
    public void SocietyStory_NeedsRankOrReputation(int rank, int reputation, int needed, bool locked)
    {
        _catalog.Add(TestQuestInfo.Create(1)
            .With(nameof(QuestInfo.AlliedSociety), EAlliedSociety.Amaljaa)
            .With(nameof(QuestInfo.AlliedSocietyRank), EAlliedSocietyRank.Recognized));
        _state.AlliedSocieties[(byte)EAlliedSociety.Amaljaa] = ((byte)rank, (ushort)reputation, (ushort)needed);

        string[] expected = locked ? ["Society reputation"] : [];
        Assert.Equal(expected, LockReasons(1));
    }

    [Theory]
    [InlineData(1u, true)]
    [InlineData(0u, false)]
    public void SocietyDaily_NeedsAnAllowance(uint allowances, bool ready)
    {
        _catalog.Add(TestQuestInfo.Create(1222)
            .With(nameof(QuestInfo.AlliedSociety), EAlliedSociety.Amaljaa)
            .With(nameof(QuestInfo.IsRepeatable), true));
        _state.AvailableAlliedSocietyQuests[EAlliedSociety.Amaljaa] = [new QuestId(1222)];
        _state.SocietyAllowances = allowances;

        Assert.Equal(ready, _functions.IsReadyToAcceptQuest(new QuestId(1222)));
        Assert.Equal(!ready, _functions.IsOutOfSocietyAllowances(new QuestId(1222)));
    }

    [Theory]
    [InlineData(49, false, false)]
    [InlineData(49, true, true)]
    [InlineData(50, false, true)]
    public void ReadyToAccept_ChecksLevelByDefault(int playerLevel, bool ignoreLevel, bool ready)
    {
        _catalog.Add(TestQuestInfo.Create(1, level: 50));
        _state.CurrentLevel = (short)playerLevel;

        Assert.Equal(ready, _functions.IsReadyToAcceptQuest(new QuestId(1), ignoreLevel));
    }

    [Fact]
    public void ReadyToAccept_RejectsUnavailable()
    {
        _catalog.Add(TestQuestInfo.Create(1));
        _catalog.Add(TestQuestInfo.Create(2, previous: [new(new QuestId(1))]));
        _catalog.Add(TestQuestInfo.Create(3).With(nameof(QuestInfo.Expansion), EExpansionVersion.Dawntrail));
        _state.MaxExpansion = (byte)EExpansionVersion.Endwalker;

        Assert.True(_functions.IsReadyToAcceptQuest(new QuestId(1)));
        Assert.False(_functions.IsReadyToAcceptQuest(new QuestId(2)));
        Assert.False(_functions.IsReadyToAcceptQuest(new QuestId(3)));
        _state.Accepted.Add(1);
        Assert.False(_functions.IsReadyToAcceptQuest(new QuestId(1)));
        _state.Accepted.Remove(1);
        _state.Complete.Add(1);
        Assert.False(_functions.IsReadyToAcceptQuest(new QuestId(1)));
        Assert.True(_functions.IsReadyToAcceptQuest(new QuestId(2)));
    }

    private void GuideShows(ushort questId, ushort level = 1)
    {
        _catalog.Add(TestQuestInfo.Create(questId, $"MSQ {questId}", level, mainScenario: true));
        _state.Guide = new(EScenarioGuideState.Ready, questId);
    }

    [Fact]
    public void EmptyGuideAtFork_PicksKnownSide()
    {
        _catalog.Add(TestQuestInfo.Create(4359, "Hitting the Books", mainScenario: true));
        _catalog.Add(TestQuestInfo.Create(4360, "For Thavnair Bound", mainScenario: true));
        _state.Guide = new(EScenarioGuideState.Ready, 0);

        Assert.Equal(new QuestId(4359), _functions.GetMainScenarioQuest().Item1.CurrentQuest);
    }

    [Theory]
    [InlineData(88, false)]
    [InlineData(89, true)]
    public void MainScenarioQuestWaitsForItsLevel(int playerLevel, bool available)
    {
        GuideShows(4449, level: 89);
        _state.CurrentLevel = (short)playerLevel;

        (QuestReference msq, string? reason) = _functions.GetMainScenarioQuest();

        ElementId? expectedQuest = available ? new QuestId(4449) : null;
        Assert.Equal(expectedQuest, msq.CurrentQuest);
        Assert.Equal(available ? MainScenarioQuestState.Available : MainScenarioQuestState.Unavailable, msq.State);
        if (!available)
            Assert.Equal("Low level", reason);
        ushort? lockedLevel = available ? null : (ushort)89;
        Assert.Equal(lockedLevel, _functions.GetLevelLockedMainScenarioQuest()?.Quest.Level);
    }

    [Fact]
    public void AcceptedClassQuest_BeforeNewMsq()
    {
        GuideShows(462);
        _state.CurrentClassJobId = (byte)Job.MRD;
        _catalog.ClassJobQuests.Add((QuestInfo)_catalog.Add(TestQuestInfo.Create(313)).Info);
        Assert.Equal(new QuestId(462), _functions.GetCurrentQuestInternal(allowNewMsq: true).CurrentQuest);

        _state.Accepted.Add(313);
        Assert.Equal(new QuestId(313), _functions.GetCurrentQuestInternal(allowNewMsq: true).CurrentQuest);
    }

    [Fact]
    public void MainScenario_SkipsDoneOrHidden()
    {
        GuideShows(10);
        _state.Complete.Add(10);
        Assert.Equal((QuestReference.NoQuest(MainScenarioQuestState.Unavailable), "Quest 10 complete"), _functions.GetMainScenarioQuest());

        _state.Complete.Remove(10);
        _state.Accepted.Add(10);
        _state.Hidden.Add(10);
        Assert.Equal((QuestReference.NoQuest(MainScenarioQuestState.Available), "Quest accepted but hidden"), _functions.GetMainScenarioQuest());
    }

    [Fact]
    public void NewGamePlus_BeatsScenarioGuide()
    {
        GuideShows(10);
        _state.Complete.Add(3759);
        _state.NewGamePlusQuestId = 20;
        _state.Sequences[20] = 2;

        Assert.Equal((new QuestReference(new QuestId(20), 2, MainScenarioQuestState.Available), "NG+"), _functions.GetMainScenarioQuest());
    }

    [Fact]
    public void ScenarioGuide_MapsToMainScenario()
    {
        _state.Guide = new(EScenarioGuideState.Missing);
        Assert.Equal(MainScenarioQuestState.Unavailable, _functions.GetMainScenarioQuest().Item1.State);
        _state.Guide = new(EScenarioGuideState.Loading);
        Assert.Equal(MainScenarioQuestState.LoadingScreen, _functions.GetMainScenarioQuest().Item1.State);
        GuideShows(10);
        _state.HasPlayer = false;
        Assert.Equal(MainScenarioQuestState.LoadingScreen, _functions.GetMainScenarioQuest().Item1.State);
    }

    // Amalj'aa dailies 1222 and 1223, both in progress; the one whose stage is in this zone goes first.
    [Theory]
    [InlineData(147u, 1223)]
    [InlineData(999u, 1222)]
    public void SocietyDailies_ThisZoneFirst(uint territory, ushort expected)
    {
        _catalog.Add(Daily(1222, EAlliedSociety.Amaljaa), QuestTestData.Seq(1, new QuestStep(EInteractionType.Interact, 1, null, 146)));
        _catalog.Add(Daily(1223, EAlliedSociety.Amaljaa), QuestTestData.Seq(1, new QuestStep(EInteractionType.Interact, 2, null, 147)));
        _state.Guide = new(EScenarioGuideState.Ready, 0);
        _state.TrackedQuestIds = [1222, 1223];
        _state.Sequences[1222] = 1;
        _state.Sequences[1223] = 1;
        _state.TerritoryType = territory;

        Assert.Equal(new QuestId(expected), _functions.GetCurrentQuestInternal(allowNewMsq: true).CurrentQuest);
    }

    [Theory]
    [InlineData(147u, 1223)]
    [InlineData(148u, 1224)]
    [InlineData(999u, 1223)]
    public void SocietyDailies_UntrackedCount(uint territory, ushort expected)
    {
        _catalog.Add(Daily(1222, EAlliedSociety.Amaljaa), QuestTestData.Seq(1, new QuestStep(EInteractionType.Interact, 1, new(1, 0, 0), 146)));
        _catalog.Add(Daily(1223, EAlliedSociety.Amaljaa), QuestTestData.Seq(1, new QuestStep(EInteractionType.Interact, 2, new(1, 0, 0), 147)));
        _catalog.Add(Daily(1224, EAlliedSociety.Amaljaa), QuestTestData.Seq(1,
            new QuestStep(EInteractionType.SwitchClass, null, null, 400),
            new QuestStep(EInteractionType.Interact, 3, new(1, 0, 0), 148)));
        _catalog.Add(Daily(1225, EAlliedSociety.Amaljaa), QuestTestData.Seq(1, new QuestStep(EInteractionType.Interact, 4, new(1, 0, 0), 147)));
        _state.Guide = new(EScenarioGuideState.Ready, 0);
        _state.TrackedQuestIds = [1222];
        _state.NormalQuests = [(1222, 1, false), (1223, 1, false), (1224, 1, false), (1225, 1, false)];
        foreach (ushort id in new ushort[] { 1222, 1223, 1224, 1225 })
            _state.Sequences[id] = 1;
        _state.TerritoryType = territory;

        Assert.Equal(new QuestId(expected), _functions.GetCurrentQuestInternal(allowNewMsq: true).CurrentQuest);
    }

    // Ixal 1518 only talks to an Ixal npc in Coerthas (155); 1503 fights in North Shroud (154).
    [Theory]
    [InlineData(154u, 1503)]
    [InlineData(155u, 1518)]
    public void SocietyNpcTalk_OnlyInThisZone(uint territory, ushort expected)
    {
        _catalog.Add(Daily(1518, EAlliedSociety.Ixal), QuestTestData.Seq(1,
            new QuestStep(EInteractionType.WalkTo, null, new(1, 0, 0), 155),
            new QuestStep(EInteractionType.Interact, 1009219, new(2, 0, 0), 155)));
        _catalog.Add(Daily(1503, EAlliedSociety.Ixal), QuestTestData.Seq(1,
            new QuestStep(EInteractionType.Combat, null, new(3, 0, 0), 154)));
        _state.Guide = new(EScenarioGuideState.Ready, 0);
        _state.TrackedQuestIds = [1518, 1503];
        _state.Sequences[1518] = 1;
        _state.Sequences[1503] = 1;
        _state.TerritoryType = territory;

        Assert.Equal(new QuestId(expected), _functions.GetCurrentQuestInternal(allowNewMsq: true).CurrentQuest);
    }

    // 1223 has its materials (stage 255, craft not done); it beats 1222's stage in this zone.
    [Fact]
    public void SocietyCraft_BeforeOtherDailies()
    {
        _catalog.Add(Daily(1222, EAlliedSociety.Amaljaa), QuestTestData.Seq(1, new QuestStep(EInteractionType.Interact, 1, new(1, 0, 0), 146)));
        _catalog.Add(Daily(1223, EAlliedSociety.Amaljaa), QuestTestData.Seq(255,
            new QuestStep(EInteractionType.Craft, null, null, 147) { ItemId = 8105, ItemCount = 4 },
            new QuestStep(EInteractionType.CompleteQuest, 2, new(2, 0, 0), 147)));
        _state.Guide = new(EScenarioGuideState.Ready, 0);
        _state.TrackedQuestIds = [1222, 1223];
        _state.Sequences[1222] = 1;
        _state.Sequences[1223] = 255;
        _state.TerritoryType = 146;
        _state.CraftMaterials.Add(8105);

        Assert.Equal(new QuestId(1223), _functions.GetCurrentQuestInternal(allowNewMsq: true).CurrentQuest);
    }

    [Fact]
    public void SocietyCraft_WaitsOnlyWithMaterials()
    {
        _catalog.Add(Daily(1222, EAlliedSociety.Amaljaa), QuestTestData.Seq(1, new QuestStep(EInteractionType.Interact, 1, new(1, 0, 0), 146)));
        _catalog.Add(Daily(1223, EAlliedSociety.Amaljaa), QuestTestData.Seq(255,
            new QuestStep(EInteractionType.Craft, null, null, 147) { ItemId = 8106, ItemCount = 4, ItemQuality = EItemQuality.HQ },
            new QuestStep(EInteractionType.CompleteQuest, 2, new(2, 0, 0), 147)));
        _state.Guide = new(EScenarioGuideState.Ready, 0);
        _state.TrackedQuestIds = [1223, 1222];
        _state.Sequences[1222] = 1;
        _state.Sequences[1223] = 255;
        _state.TerritoryType = 146;

        Assert.Equal(new QuestId(1222), _functions.GetCurrentQuestInternal(allowNewMsq: true).CurrentQuest);

        _state.CraftMaterials.Add(8106);
        Assert.Equal(new QuestId(1223), _functions.GetCurrentQuestInternal(allowNewMsq: true).CurrentQuest);
    }

    [Fact]
    public void EmptyGuide_FallsBackToReadyQuest()
    {
        _catalog.Add(TestQuestInfo.Create(30, mainScenario: true));
        _catalog.Add(TestQuestInfo.Create(31, mainScenario: true, previous: [new(new QuestId(30))]));
        _state.Guide = new(EScenarioGuideState.Ready, 0);

        Assert.Equal(new QuestId(30), _functions.GetMainScenarioQuest().Item1.CurrentQuest);

        _state.Complete.Add(30);
        Assert.Equal(new QuestId(31), _functions.GetMainScenarioQuest().Item1.CurrentQuest);

        _catalog.LastMainScenarioQuestId = new QuestId(31);
        _state.Complete.Add(31);
        Assert.Equal(MainScenarioQuestState.Complete, _functions.GetMainScenarioQuest().Item1.State);
    }
}
