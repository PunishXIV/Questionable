using Dalamud.Plugin.Services;
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

    private string[] LockReasons(ushort questId) => _functions.IsQuestLocked(new QuestId(questId)).reasons!;

    [Fact]
    public void AllPreviousQuestsMustBeDoneAndTheMissingOnesAreNamed()
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
    public void PreviousQuestInProgressCountsOnceItReachesTheSequence(int sequence, bool locked)
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
    public void SpecialPrerequisiteReasonComesFromTheTable()
    {
        _catalog.Add(TestQuestInfo.Create(576));
        _state.ChocoboRacingRank = 39;

        Assert.Equal(["Needs chocobo racing rank 40"], LockReasons(576));
        _state.ChocoboRacingRank = 40;
        Assert.Empty(LockReasons(576));
    }

    [Fact]
    public void LockedAetheryteShortcutLocksTheQuestUnlessTheQuestAttunesItFirst()
    {
        var shortcut = new QuestStep { TerritoryId = 956, AetheryteShortcut = EAetheryteLocation.LabyrinthosAporia };
        _catalog.Add(TestQuestInfo.Create(1), QuestTestData.Seq(0, shortcut));
        _catalog.Add(TestQuestInfo.Create(2), QuestTestData.Seq(0,
            new QuestStep { InteractionType = EInteractionType.AttuneAetheryte, Aetheryte = EAetheryteLocation.LabyrinthosAporia },
            shortcut));
        _state.LockedAetherytes.Add(EAetheryteLocation.LabyrinthosAporia);

        Assert.Equal(["Aetheryte locked: LabyrinthosAporia"], LockReasons(1));
        Assert.Empty(LockReasons(2));
    }

    [Fact]
    public void LockedCityAethernetNamesTheShardUntilItsUnlockQuestIsStarted()
    {
        _catalog.Add(TestQuestInfo.Create(1), QuestTestData.Seq(0,
            new QuestStep { TerritoryId = 129, AetheryteShortcut = EAetheryteLocation.LimsaZephyrGate }));
        _state.LockedAetherytes.Add(EAetheryteLocation.LimsaZephyrGate);

        Assert.Equal(["Aethernet locked (l): LimsaZephyrGate"], LockReasons(1));
        _state.Accepted.Add(108);
        Assert.Empty(LockReasons(1));
    }

    [Theory]
    [InlineData(2, 100, 100, false)]
    [InlineData(2, 99, 100, true)]
    [InlineData(3, 0, 100, false)]
    public void AlliedSocietyStoryQuestNeedsRankOrReputation(int rank, int reputation, int needed, bool locked)
    {
        _catalog.Add(TestQuestInfo.Create(1)
            .With(nameof(QuestInfo.AlliedSociety), EAlliedSociety.Amaljaa)
            .With(nameof(QuestInfo.AlliedSocietyRank), EAlliedSocietyRank.Recognized));
        _state.AlliedSocieties[(byte)EAlliedSociety.Amaljaa] = ((byte)rank, (ushort)reputation, (ushort)needed);

        string[] expected = locked ? ["Society rep"] : [];
        Assert.Equal(expected, LockReasons(1));
    }

    [Theory]
    [InlineData(49, false, false)]
    [InlineData(49, true, true)]
    [InlineData(50, false, true)]
    public void ReadyToAcceptChecksTheLevelUnlessAskedNotTo(int playerLevel, bool ignoreLevel, bool ready)
    {
        _catalog.Add(TestQuestInfo.Create(1, level: 50));
        _state.CurrentLevel = (short)playerLevel;

        Assert.Equal(ready, _functions.IsReadyToAcceptQuest(new QuestId(1), ignoreLevel));
    }

    [Fact]
    public void ReadyToAcceptRejectsAcceptedCompleteLockedAndUnreleasedQuests()
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
    public void MainScenarioQuestThatIsCompleteOrHiddenIsNotPicked()
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
    public void NewGamePlusChapterWinsOverTheScenarioGuide()
    {
        GuideShows(10);
        _state.Complete.Add(3759);
        _state.NewGamePlusQuestId = 20;
        _state.Sequences[20] = 2;

        Assert.Equal((new QuestReference(new QuestId(20), 2, MainScenarioQuestState.Available), "NG+"), _functions.GetMainScenarioQuest());
    }

    [Fact]
    public void ScenarioGuideStatesMapToMainScenarioStates()
    {
        _state.Guide = new(EScenarioGuideState.Missing);
        Assert.Equal(MainScenarioQuestState.Unavailable, _functions.GetMainScenarioQuest().Item1.State);
        _state.Guide = new(EScenarioGuideState.Loading);
        Assert.Equal(MainScenarioQuestState.LoadingScreen, _functions.GetMainScenarioQuest().Item1.State);
        GuideShows(10);
        _state.HasPlayer = false;
        Assert.Equal(MainScenarioQuestState.LoadingScreen, _functions.GetMainScenarioQuest().Item1.State);
    }

    [Fact]
    public void EmptyGuideFallsBackToTheOnlyReadyMainScenarioQuest()
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
