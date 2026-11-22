using ECommons.ExcelServices;
using Quest = Questionable.Domain.Quest;
using EAutomationType = Questionable.Controller.QuestController.EAutomationType;
using ECurrentQuestType = Questionable.Controller.QuestController.ECurrentQuestType;
using QuestProgress = Questionable.Controller.QuestController.QuestProgress;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class QuestSelectionController(Configuration configuration)
{
    private static readonly IQuestRule[] Rules =
        [new SimulatedRule(), new NextRule(), new PriorityRule(), new JournalRule()];

    public static bool ShouldQueueAcceptOnly(QuestSlots slots, EAutomationType automation) =>
        slots.Simulated == null && slots.Next == null &&
        (automation == EAutomationType.Automatic || slots.Started == null);

    public static Quest? PickAcceptOnly(IEnumerable<Quest> acceptOnlyQuests, IQuestSelectionState state, uint? territory = null) =>
        acceptOnlyQuests
            .Where(QuestFunctions.CanQueueSociety)
            .Where(x => state.IsReadyToAccept(x.Id, ignoreLevel: true) && state.HasClassJobAtQuestLevel(x.Id))
            .OrderByDescending(x => x.Info is QuestInfo info ? info.AlliedSocietyRank : EAlliedSocietyRank.None)
            .ThenBy(x => x.FindSequence(0)?.FindStep(0)?.TerritoryId == territory ? 0 : 1)
            .FirstOrDefault();

    // Completed repeatables can still be accepted, so the quest must be both complete and no longer accepted.
    public EStopPoint? GetStopPoint(QuestProgress? started, bool stopAfterCurrentQuest, IQuestSelectionState state)
    {
        if (started == null || !state.IsComplete(started.Quest.Id) || state.IsAccepted(started.Quest.Id))
            return null;
        if (configuration.Stop.Enabled && configuration.Stop.QuestsToStopAfter.Contains(started.Quest.Id))
            return EStopPoint.Configured;
        return stopAfterCurrentQuest ? EStopPoint.AfterCurrentQuest : null;
    }

    public static QuestChoice Choose(QuestSelectionContext context) => Rules.Select(x => x.Offer(context)).First(x => x != null)!;

    public static bool IsLevelTooHigh(int playerLevel, Quest quest) => playerLevel < quest.Info.Level;

    public static int BestLevel(int currentLevel, IReadOnlyList<Job> questJobs,
        IEnumerable<(Job ClassJob, short Level, short ItemLevel)> gearSets) =>
        gearSets.Where(x => questJobs.Count == 0 || questJobs.Contains(x.ClassJob))
            .Select(x => (int)x.Level)
            .Append(currentLevel)
            .Max();

    // Busy means in a duty or waiting on another plugin: the stop waits so neither is left half done.
    public static bool IsLevelStopDue(int level, int target, bool running, bool busy) =>
        running && !busy && level >= target;

    // Quest state read during a teleport can briefly offer another quest (e.g. a priority quest).
    internal static bool ShouldHoldStartedQuest(QuestProgress? started, bool betweenAreas) => started != null && betweenAreas;

    internal static QuestProgress ResumeOrStart(QuestProgress? displaced, Quest quest, byte sequence) =>
        displaced != null && displaced.Quest.Id == quest.Id && displaced.Sequence == sequence
            ? new(quest, sequence, displaced.Step)
            : new(quest, sequence);

    // Leaving a duty, or a quest that can't be resumed from elsewhere (e.g. the ferry-only Isles of Umbre), strands the player.
    internal static bool CanSwitchToPriorityQuest(QuestProgress? started, ElementId priorityQuestId, bool inDuty) =>
        started == null || started.Quest.Id == priorityQuestId || (!inDuty && started.Quest.Root.Interruptible);

    public static bool IsInterruptible((QuestProgress Progress, ECurrentQuestType Type)? details,
        EAutomationType automation, bool hasExternalOperation, Func<Quest, bool> isPriority)
    {
        if (hasExternalOperation)
            return false;

        if (automation is EAutomationType.SingleQuestA or EAutomationType.SingleQuestB)
            return false;

        if (details == null)
            return false;

        (QuestProgress currentQuest, ECurrentQuestType type) = details.Value;
        if (type != ECurrentQuestType.Normal || !currentQuest.Quest.Root.Interruptible || currentQuest.Sequence == 0)
            return false;

        if (isPriority(currentQuest.Quest))
            return false;

        // "Ifrit Bleeds, We Can Kill It" is accepted during the MSQ 'Moving On', so it isn't a priority quest.
        if (QuestData.HardModePrimals.Contains(currentQuest.Quest.Id))
            return false;

        if (currentQuest.Quest.Info.AlliedSociety != EAlliedSociety.None)
            return false;

        if (currentQuest.Step > 0)
            return false;

        QuestStep? previous = currentQuest.Quest.Root.QuestSequence
            .Where(x => x.Sequence < currentQuest.Sequence)
            .MaxBy(x => x.Sequence)?.Steps.LastOrDefault();
        return currentQuest.FindStep() is { } currentStep && previous != null &&
               currentStep.TerritoryId != (previous.TargetTerritoryId ?? previous.TerritoryId);
    }

    // Both halves of a city are one aethernet hop apart.
    private static readonly uint[][] Cities = [[128, 129], [130, 131], [132, 133], [418, 419]];

    internal static bool IsSameCity(uint a, uint? b) =>
        a == b || Cities.Any(x => x.Contains(a) && b is { } other && x.Contains(other));

    internal static bool IsFreeClassQuestDetour(QuestStep classStart, uint? territory, QuestStep? teleportStep)
    {
        if (IsSameCity(classStart.TerritoryId, territory))
            return true;
        return teleportStep != null && !IsSameCity(teleportStep.TerritoryId, territory) &&
               IsSameCity(classStart.TerritoryId, teleportStep.TerritoryId);
    }

    internal static bool IsOtherQuestRunning(QuestProgress? started, ElementId priorityQuestId, IQuestSelectionState state,
        Func<ElementId?> journalQuest)
    {
        bool onlyPicked = started?.Quest.Id == priorityQuestId && !state.IsAccepted(priorityQuestId);
        if (started != null && !onlyPicked)
            return started.Quest.Id != priorityQuestId &&
                   (!state.IsComplete(started.Quest.Id) || state.IsAccepted(started.Quest.Id));

        return journalQuest() is { } questId && questId != priorityQuestId && state.IsAccepted(questId);
    }

    public static ElementId? PickPriorityQuest(QuestSlots slots, bool stopRequested,
        Func<IEnumerable<PriorityQuestInfo>> priorityQuests)
    {
        if (slots.Next != null || slots.Simulated != null || stopRequested)
            return null;

        ElementId? questId = priorityQuests()
            .Where(x => x.IsAvailable)
            .Select(x => x.QuestId)
            .FirstOrDefault();

        if (questId == null || (slots.Started != null && questId == slots.Started.Quest.Id))
            return null;

        return questId;
    }

    internal static bool IsPastPriorityList(QuestChoice choice, IReadOnlyList<Quest> priorityQuests,
        Func<ElementId, bool> waitingToAccept) =>
        choice is { Source: EQuestSource.Journal or EQuestSource.Next, QuestId: { } questId } &&
        priorityQuests.All(x => x.Id != questId && !waitingToAccept(x.Id));

    private sealed class SimulatedRule : IQuestRule
    {
        public QuestChoice? Offer(QuestSelectionContext context) =>
            context.Slots.Simulated is { } quest ? QuestChoice.ForSlot(EQuestSource.Simulated, quest) : null;
    }

    private sealed class NextRule : IQuestRule
    {
        public QuestChoice? Offer(QuestSelectionContext context) =>
            context.Slots.Next is { } quest && context.State.IsReadyToAccept(quest.Quest.Id) &&
            !SetAsideQuests.Contains(quest.Quest.Id, quest.Sequence)
                ? QuestChoice.ForSlot(EQuestSource.Next, quest)
                : null;
    }

    private sealed class PriorityRule : IQuestRule
    {
        public QuestChoice? Offer(QuestSelectionContext context)
        {
            Quest? candidate = context.PriorityQuests
                .Where(x => !context.IsAcceptOnly(x.Id) && !SetAsideQuests.Contains(x.Id, context.State.GetSequence(x.Id)))
                .FirstOrDefault(x => context.State.IsReadyToAccept(x.Id, ignoreLevel: true) || context.State.IsAccepted(x.Id));
            if (candidate == null || !CanSwitchToPriorityQuest(context.Slots.Started, candidate.Id, context.State.InDuty))
                return null;

            if (!context.CanInterrupt &&
                IsOtherQuestRunning(context.Slots.Started, candidate.Id, context.State, () => context.Current.CurrentQuest))
                return null;

            return new(EQuestSource.Priority, null, candidate.Id, context.State.GetSequence(candidate.Id),
                context.Current.State);
        }
    }

    private sealed class JournalRule : IQuestRule
    {
        public QuestChoice Offer(QuestSelectionContext context) =>
            new(EQuestSource.Journal, null, context.Current.CurrentQuest, context.Current.Sequence, context.Current.State);
    }
}

internal interface IQuestRule
{
    QuestChoice? Offer(QuestSelectionContext context);
}

internal interface IQuestSelectionState
{
    bool InDuty { get; }
    bool BetweenAreas { get; }
    bool IsAccepted(ElementId questId);
    bool IsComplete(ElementId questId);
    bool IsReadyToAccept(ElementId questId, bool ignoreLevel = false);
    bool HasClassJobAtQuestLevel(ElementId questId);
    byte GetSequence(ElementId questId);
    QuestReference GetCurrentQuest(bool allowNewMsq, bool skipPriority = false);
}

internal sealed record QuestSlots(QuestProgress? Started, QuestProgress? Next, QuestProgress? Simulated);

internal sealed class QuestSelectionContext(
    QuestSlots slots,
    IReadOnlyList<Quest> priorityQuests,
    Func<ElementId, bool> isAcceptOnly,
    EAutomationType automation,
    IQuestSelectionState state,
    bool canInterrupt = true)
{
    private QuestReference? _current;

    public QuestSlots Slots => slots;
    public IReadOnlyList<Quest> PriorityQuests => priorityQuests;
    public Func<ElementId, bool> IsAcceptOnly => isAcceptOnly;
    public IQuestSelectionState State => state;
    public bool CanInterrupt => canInterrupt;
    // The journal lookup puts priority quests ahead of an accepted MSQ; with interrupting off the MSQ has to win.
    public QuestReference Current => _current ??= state.GetCurrentQuest(
        allowNewMsq: automation != EAutomationType.SingleQuestB, skipPriority: !canInterrupt);
}

internal enum EQuestSource
{
    Simulated,
    Next,
    Priority,
    Journal,
}

internal enum EStopPoint
{
    Configured,
    AfterCurrentQuest,
}

internal sealed record QuestChoice(
    EQuestSource Source,
    QuestProgress? Progress,
    ElementId? QuestId,
    byte Sequence,
    MainScenarioQuestState MsqState = default)
{
    public static QuestChoice ForSlot(EQuestSource source, QuestProgress quest) =>
        new(source, quest, quest.Quest.Id, quest.Sequence);
}
