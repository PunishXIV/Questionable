using NSubstitute;
using Questionable.Controller;
using Questionable.Domain;
using Questionable.Functions;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;
using EAutomationType = Questionable.Controller.QuestController.EAutomationType;
using ECurrentQuestType = Questionable.Controller.QuestController.ECurrentQuestType;
using QuestProgress = Questionable.Controller.QuestController.QuestProgress;

namespace Questionable.Tests.Steps;

public sealed class QuestSelectionControllerTests
{
    private readonly Configuration _configuration = new();
    private readonly QuestSelectionController _selection;
    private readonly FakeState _state = new();
    private readonly HashSet<ElementId> _acceptOnly = [];

    public QuestSelectionControllerTests() => _selection = new(_configuration);

    private static Quest NewQuest(ushort id, int level = 1, bool interruptible = true)
    {
        Quest quest = QuestTestData.CreateQuest(new QuestId(id));
        quest.Info.Level.Returns((ushort)level);
        quest.Root.Interruptible = interruptible;
        return quest;
    }

    private static QuestProgress Progress(ushort id, byte sequence = 1, bool interruptible = true) =>
        new(NewQuest(id, interruptible: interruptible), sequence);

    private QuestChoice Choose(QuestSlots slots, EAutomationType automation = EAutomationType.Automatic, params Quest[] priority) =>
        QuestSelectionController.Choose(new QuestSelectionContext(slots, priority, _acceptOnly.Contains, automation, _state));

    private QuestChoice ChooseWaiting(QuestSlots slots, params Quest[] priority) =>
        QuestSelectionController.Choose(new QuestSelectionContext(slots, priority, _acceptOnly.Contains,
            EAutomationType.Automatic, _state, canInterrupt: false));

    private static QuestSlots Slots(QuestProgress? started = null, QuestProgress? next = null, QuestProgress? gathering = null,
        QuestProgress? simulated = null) => new(started, next, gathering, simulated);

    [Fact]
    public void SimulatedBeatsEveryOtherSource()
    {
        QuestProgress simulated = Progress(1, sequence: 3);
        _state.Ready.Add(new QuestId(2));

        QuestChoice choice = Choose(Slots(next: Progress(2), gathering: Progress(3), simulated: simulated), priority: [NewQuest(4)]);

        Assert.Equal(EQuestSource.Simulated, choice.Source);
        Assert.Same(simulated, choice.Progress);
        Assert.Equal(3, choice.Sequence);
        Assert.Equal(0, _state.CurrentQueries);
    }

    [Fact]
    public void NextRunsOnlyOnceItCanBeAccepted()
    {
        QuestProgress next = Progress(2);
        QuestProgress gathering = Progress(3);

        Assert.Equal(EQuestSource.Gathering, Choose(Slots(next: next, gathering: gathering)).Source);

        _state.Ready.Add(next.Quest.Id);
        QuestChoice choice = Choose(Slots(next: next, gathering: gathering));
        Assert.Equal(EQuestSource.Next, choice.Source);
        Assert.Same(next, choice.Progress);
    }

    [Fact]
    public void JournalQuestRunsWhenNothingElseOffers()
    {
        _state.Current = new QuestReference(new QuestId(70), 2, MainScenarioQuestState.Available);

        QuestChoice choice = Choose(Slots(started: Progress(70)));

        Assert.Equal(EQuestSource.Journal, choice.Source);
        Assert.Null(choice.Progress);
        Assert.Equal(new QuestId(70), choice.QuestId);
        Assert.Equal(2, choice.Sequence);
        Assert.Equal(MainScenarioQuestState.Available, choice.MsqState);
    }

    [Theory]
    [InlineData((int)EAutomationType.Automatic, true)]
    [InlineData((int)EAutomationType.SingleQuestA, true)]
    [InlineData((int)EAutomationType.SingleQuestB, false)]
    public void FinishedSingleQuest_StopsMainScenario(int automation, bool allowNewMsq)
    {
        Choose(Slots(), (EAutomationType)automation);

        Assert.Equal(allowNewMsq, _state.LastAllowNewMsq);
    }

    [Fact]
    public void ReadyPriorityQuest_OverridesJournal()
    {
        _state.Current = QuestReference.NoQuest(MainScenarioQuestState.Complete);
        Quest notReady = NewQuest(10);
        Quest ready = NewQuest(11);
        _state.ReadyIgnoringLevel.Add(ready.Id);
        _state.Sequences[ready.Id] = 4;

        QuestChoice choice = Choose(Slots(), priority: [notReady, ready]);

        Assert.Equal(EQuestSource.Priority, choice.Source);
        Assert.Equal(ready.Id, choice.QuestId);
        Assert.Equal(4, choice.Sequence);
        Assert.Equal(MainScenarioQuestState.Complete, choice.MsqState);

        _state.ReadyIgnoringLevel.Clear();
        _state.Accepted.Add(notReady.Id);
        Assert.Equal(notReady.Id, Choose(Slots(), priority: [notReady, ready]).QuestId);
    }

    [Fact]
    public void AcceptOnlyPriority_LeftToAcceptQueue()
    {
        Quest acceptOnly = NewQuest(10);
        _acceptOnly.Add(acceptOnly.Id);
        _state.Ready.Add(acceptOnly.Id);

        Assert.Equal(EQuestSource.Journal, Choose(Slots(), priority: [acceptOnly]).Source);
    }

    [Fact]
    public void PriorityQuests_OnlyFirstEligible()
    {
        // The started quest can't be interrupted, so the first candidate is refused even though the second is the started one.
        _state.Current = new QuestReference(new QuestId(11), 1, MainScenarioQuestState.Available);
        Quest first = NewQuest(10);
        Quest second = NewQuest(11);
        _state.Ready.UnionWith([first.Id, second.Id]);

        QuestChoice choice = Choose(Slots(started: Progress(11, interruptible: false)), priority: [first, second]);

        Assert.Equal(EQuestSource.Journal, choice.Source);
    }

    [Theory]
    [InlineData(true, false, (int)EQuestSource.Priority)]
    [InlineData(true, true, (int)EQuestSource.Journal)]
    [InlineData(false, false, (int)EQuestSource.Journal)]
    public void PriorityQuest_OnlyInterruptsOutsideDuty(bool interruptible, bool inDuty, int expected)
    {
        Quest priority = NewQuest(10);
        _state.Ready.Add(priority.Id);
        _state.InDuty = inDuty;

        Assert.Equal((EQuestSource)expected, Choose(Slots(started: Progress(70, interruptible: interruptible)), priority: [priority]).Source);
    }

    [Fact]
    public void NoInterrupt_WaitsForStartedQuest()
    {
        Quest priority = NewQuest(10);
        QuestProgress started = Progress(70);
        _state.Ready.Add(priority.Id);

        Assert.Equal(EQuestSource.Journal, ChooseWaiting(Slots(started: started), priority).Source);
        _state.Complete.Add(started.Quest.Id);
        _state.Accepted.Add(started.Quest.Id); // a completed repeatable picked up again
        Assert.Equal(EQuestSource.Journal, ChooseWaiting(Slots(started: started), priority).Source);
        _state.Accepted.Remove(started.Quest.Id);
        Assert.Equal(EQuestSource.Priority, ChooseWaiting(Slots(started: started), priority).Source);
    }

    [Fact]
    public void NoInterrupt_KeepsRunningPriority()
    {
        Quest priority = NewQuest(10);
        _state.Accepted.Add(priority.Id);

        Assert.Equal(EQuestSource.Priority, ChooseWaiting(Slots(started: new(priority, 1)), priority).Source);
    }

    [Fact]
    public void NoInterrupt_AcceptedMsqBeatsPriority()
    {
        Quest priority = NewQuest(10);
        var msq = new QuestId(70);
        _state.Ready.Add(priority.Id);
        _state.Accepted.Add(msq);
        _state.Current = new QuestReference(priority.Id, 0, MainScenarioQuestState.Available);
        _state.WithoutPriority = new QuestReference(msq, 1, MainScenarioQuestState.Available);

        QuestChoice waiting = ChooseWaiting(Slots(), priority);
        Assert.Equal(EQuestSource.Journal, waiting.Source);
        Assert.Equal(msq, waiting.QuestId);
        Assert.Equal(EQuestSource.Priority, Choose(Slots(), priority: priority).Source);
    }

    [Fact]
    public void NoInterrupt_PickedPriorityYieldsToMsq()
    {
        Quest priority = NewQuest(10);
        var msq = new QuestId(70);
        _state.Ready.Add(priority.Id);
        _state.Accepted.Add(msq);
        _state.WithoutPriority = new QuestReference(msq, 1, MainScenarioQuestState.Available);

        QuestChoice waiting = ChooseWaiting(Slots(started: new(priority, 0)), priority);
        Assert.Equal(EQuestSource.Journal, waiting.Source);
        Assert.Equal(msq, waiting.QuestId);
    }

    [Fact]
    public void NoInterrupt_AfterStopWaitsForJournal()
    {
        Quest priority = NewQuest(10);
        _state.Ready.Add(priority.Id);
        _state.Current = new QuestReference(new QuestId(70), 1, MainScenarioQuestState.Available);

        Assert.Equal(EQuestSource.Priority, ChooseWaiting(Slots(), priority).Source);
        _state.Accepted.Add(new QuestId(70));
        Assert.Equal(EQuestSource.Journal, ChooseWaiting(Slots(), priority).Source);
    }

    [Theory]
    [InlineData((int)EAutomationType.Automatic, false, true)]
    [InlineData((int)EAutomationType.Automatic, true, true)]
    [InlineData((int)EAutomationType.Manual, false, true)]
    [InlineData((int)EAutomationType.Manual, true, false)]
    [InlineData((int)EAutomationType.SingleQuestA, true, false)]
    public void AcceptOnlyQueue_RunsIfAutoOrIdle(int automation, bool started, bool expected)
    {
        Assert.Equal(expected, QuestSelectionController.ShouldQueueAcceptOnly(Slots(started: started ? Progress(1) : null),
            (EAutomationType)automation));
    }

    [Fact]
    public void AcceptOnlyQueueWaitsForOtherSlots()
    {
        Assert.False(QuestSelectionController.ShouldQueueAcceptOnly(Slots(next: Progress(1)), EAutomationType.Automatic));
        Assert.False(QuestSelectionController.ShouldQueueAcceptOnly(Slots(gathering: Progress(1)), EAutomationType.Automatic));
        Assert.False(QuestSelectionController.ShouldQueueAcceptOnly(Slots(simulated: Progress(1)), EAutomationType.Automatic));
    }

    [Fact]
    public void AcceptOnly_PicksFirstReadyForClass()
    {
        Quest wrongClass = NewQuest(10);
        Quest notReady = NewQuest(11);
        Quest pick = NewQuest(12);
        _state.ReadyIgnoringLevel.UnionWith([wrongClass.Id, pick.Id]);
        _state.ClassJob.UnionWith([notReady.Id, pick.Id]);

        Assert.Same(pick, QuestSelectionController.PickAcceptOnly([wrongClass, notReady, pick], _state));
        Assert.Null(QuestSelectionController.PickAcceptOnly([wrongClass, notReady], _state));
    }

    [Theory]
    [InlineData(true, true, false, (int)EStopPoint.Configured)]
    [InlineData(true, true, true, (int)EStopPoint.Configured)]
    [InlineData(false, true, true, (int)EStopPoint.AfterCurrentQuest)]
    [InlineData(true, false, true, (int)EStopPoint.AfterCurrentQuest)]
    [InlineData(false, true, false, null)]
    public void StopPointNeedsTheQuestDoneAndGone(bool enabled, bool listed, bool stopAfterCurrent, int? expected)
    {
        QuestProgress started = Progress(70);
        _configuration.Stop.Enabled = enabled;
        if (listed)
            _configuration.Stop.QuestsToStopAfter.Add(started.Quest.Id);
        _state.Complete.Add(started.Quest.Id);

        Assert.Equal((EStopPoint?)expected, _selection.GetStopPoint(started, stopAfterCurrent, _state));
    }

    [Fact]
    public void StopPoint_NotWhileQuestUnfinished()
    {
        QuestProgress started = Progress(70);
        _configuration.Stop.Enabled = true;
        _configuration.Stop.QuestsToStopAfter.Add(started.Quest.Id);

        Assert.Null(_selection.GetStopPoint(started, stopAfterCurrentQuest: true, _state));
        _state.Complete.Add(started.Quest.Id);
        _state.Accepted.Add(started.Quest.Id); // a completed repeatable picked up again
        Assert.Null(_selection.GetStopPoint(started, stopAfterCurrentQuest: true, _state));
        Assert.Null(_selection.GetStopPoint(null, stopAfterCurrentQuest: true, _state));
    }

    [Theory]
    [InlineData(34, 35, true)]
    [InlineData(35, 35, false)]
    [InlineData(36, 35, false)]
    public void LevelIsTooHighOnlyBelowTheQuestLevel(int playerLevel, int questLevel, bool expected)
    {
        Assert.Equal(expected, QuestSelectionController.IsLevelTooHigh(playerLevel, NewQuest(10, questLevel)));
    }

    [Fact]
    public void StartedQuest_HeldOnlyWhileLoading()
    {
        Assert.True(QuestSelectionController.ShouldHoldStartedQuest(Progress(10), betweenAreas: true));
        Assert.False(QuestSelectionController.ShouldHoldStartedQuest(Progress(10), betweenAreas: false));
        Assert.False(QuestSelectionController.ShouldHoldStartedQuest(null, betweenAreas: true));
    }

    [Fact]
    public void SwitchBack_ResumesDisplacedStep()
    {
        QuestProgress displaced = new(NewQuest(10), 255, step: 1);

        QuestProgress resumed = QuestSelectionController.ResumeOrStart(displaced, displaced.Quest, 255);
        Assert.Equal(1, resumed.Step);

        Assert.Equal(0, QuestSelectionController.ResumeOrStart(displaced, displaced.Quest, 2).Step);
        Assert.Equal(0, QuestSelectionController.ResumeOrStart(displaced, NewQuest(11), 255).Step);
        Assert.Equal(0, QuestSelectionController.ResumeOrStart(null, displaced.Quest, 255).Step);
    }

    private static (QuestProgress Progress, ECurrentQuestType Type)? Details(ushort id = 500, bool interruptible = true,
        byte sequence = 1, int step = 0, EAlliedSociety society = EAlliedSociety.None, bool shortcut = true,
        SkipConditions? skip = null, ECurrentQuestType type = ECurrentQuestType.Normal)
    {
        QuestStep NewStep() => new()
        {
            AetheryteShortcut = shortcut ? default(EAetheryteLocation) : null,
            SkipConditions = skip,
        };

        Quest quest = QuestTestData.CreateQuest(new QuestId(id), QuestTestData.Seq(sequence, NewStep(), NewStep()));
        quest.Root.Interruptible = interruptible;
        quest.Info.AlliedSociety.Returns(society);
        return (new QuestProgress(quest, sequence, step), type);
    }

    private static bool Interruptible((QuestProgress Progress, ECurrentQuestType Type)? details,
        EAutomationType automation = EAutomationType.Automatic, bool hasExternalOperation = false,
        bool isPriority = false) =>
        QuestSelectionController.IsInterruptible(details, automation, hasExternalOperation, _ => isPriority);

    [Fact]
    public void Interruptible_FirstStepWithShortcut()
    {
        Assert.True(Interruptible(Details()));
    }

    [Fact]
    public void Interruptible_FalseForEachBlocker()
    {
        Assert.False(Interruptible(Details(), hasExternalOperation: true));
        Assert.False(Interruptible(Details(), EAutomationType.SingleQuestA));
        Assert.False(Interruptible(Details(), EAutomationType.SingleQuestB));
        Assert.False(Interruptible(null));
        Assert.False(Interruptible(Details(type: ECurrentQuestType.Simulated)));
        Assert.False(Interruptible(Details(interruptible: false)));
        Assert.False(Interruptible(Details(sequence: 0)));
        Assert.False(Interruptible(Details(), isPriority: true));
        Assert.False(Interruptible(Details(id: 1048)));
        Assert.False(Interruptible(Details(society: EAlliedSociety.Amaljaa)));
        Assert.False(Interruptible(Details(step: 1)));
        Assert.False(Interruptible(Details(shortcut: false)));
    }

    [Fact]
    public void Interruptible_FalseIfShortcutCanBeSkipped()
    {
        Assert.False(Interruptible(Details(skip: new() { AetheryteShortcutIf = new() { QuestsCompleted = [new QuestId(1)] } })));
        Assert.False(Interruptible(Details(skip: new() { AetheryteShortcutIf = new() { QuestsAccepted = [new QuestId(1)] } })));
        Assert.True(Interruptible(Details(skip: new() { AetheryteShortcutIf = new() })));
    }

    [Fact]
    public void Pick_FirstAvailablePriority()
    {
        ElementId? picked = QuestSelectionController.PickPriorityQuest(Slots(), false,
            () => [new PriorityQuestInfo(new QuestId(1), "locked"), new PriorityQuestInfo(new QuestId(2)), new PriorityQuestInfo(new QuestId(3))]);

        Assert.Equal(new QuestId(2), picked);
    }

    [Fact]
    public void Pick_NoneAvailable_ReturnsNull()
    {
        Assert.Null(QuestSelectionController.PickPriorityQuest(Slots(), false,
            () => [new PriorityQuestInfo(new QuestId(1), "locked")]));
    }

    [Fact]
    public void Pick_SameAsStarted_ReturnsNull()
    {
        Assert.Null(QuestSelectionController.PickPriorityQuest(Slots(started: Progress(2)), false,
            () => [new PriorityQuestInfo(new QuestId(2))]));
    }

    [Fact]
    public void Pick_BlockedNeverReadsList()
    {
        IEnumerable<PriorityQuestInfo> Unread() => throw new InvalidOperationException();

        Assert.Null(QuestSelectionController.PickPriorityQuest(Slots(next: Progress(2)), false, Unread));
        Assert.Null(QuestSelectionController.PickPriorityQuest(Slots(gathering: Progress(2)), false, Unread));
        Assert.Null(QuestSelectionController.PickPriorityQuest(Slots(simulated: Progress(2)), false, Unread));
        Assert.Null(QuestSelectionController.PickPriorityQuest(Slots(), true, Unread));
    }

    private sealed class FakeState : IQuestSelectionState
    {
        public bool InDuty { get; set; }
        public bool BetweenAreas { get; set; }
        public HashSet<ElementId> Accepted { get; } = [];
        public HashSet<ElementId> Complete { get; } = [];
        public HashSet<ElementId> Ready { get; } = [];
        public HashSet<ElementId> ReadyIgnoringLevel { get; } = [];
        public HashSet<ElementId> ClassJob { get; } = [];
        public Dictionary<ElementId, byte> Sequences { get; } = [];
        public QuestReference Current { get; set; } = QuestReference.NoQuest(MainScenarioQuestState.Available);
        public QuestReference? WithoutPriority { get; set; }
        public int CurrentQueries { get; private set; }
        public bool? LastAllowNewMsq { get; private set; }

        public bool IsAccepted(ElementId questId) => Accepted.Contains(questId);
        public bool IsComplete(ElementId questId) => Complete.Contains(questId);

        public bool IsReadyToAccept(ElementId questId, bool ignoreLevel = false) =>
            Ready.Contains(questId) || (ignoreLevel && ReadyIgnoringLevel.Contains(questId));

        public bool HasClassJobAtQuestLevel(ElementId questId) => ClassJob.Contains(questId);
        public byte GetSequence(ElementId questId) => Sequences.GetValueOrDefault(questId);

        public QuestReference GetCurrentQuest(bool allowNewMsq, bool skipPriority = false)
        {
            CurrentQueries++;
            LastAllowNewMsq = allowNewMsq;
            return skipPriority && WithoutPriority != null ? WithoutPriority : Current;
        }
    }
}
