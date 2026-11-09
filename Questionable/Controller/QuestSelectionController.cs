using Questionable.Data;
using Questionable.Functions;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Quest = Questionable.Domain.Quest;
using EAutomationType = Questionable.Controller.QuestController.EAutomationType;
using ECurrentQuestType = Questionable.Controller.QuestController.ECurrentQuestType;
using QuestProgress = Questionable.Controller.QuestController.QuestProgress;

namespace Questionable.Controller;

/// <summary>Decides which quest runs and when questing stops; QuestController reads the game and applies the result.</summary>
[RegisterSingleton]
internal sealed class QuestSelectionController(Configuration configuration)
{
    private static readonly IQuestRule[] Rules =
        [new SimulatedRule(), new NextRule(), new GatheringRule(), new PriorityRule(), new JournalRule()];

    public static bool ShouldQueueAcceptOnly(QuestSlots slots, EAutomationType automation) =>
        slots.Simulated == null && slots.Next == null && slots.Gathering == null &&
        (automation == EAutomationType.Automatic || slots.Started == null);

    public static Quest? PickAcceptOnly(IEnumerable<Quest> acceptOnlyQuests, IQuestSelectionState state) =>
        acceptOnlyQuests.FirstOrDefault(x => state.IsReadyToAccept(x.Id, ignoreLevel: true) && state.HasClassJobAtQuestLevel(x.Id));

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

        // "ifrit bleeds, we can kill it" isn't listed as priority quest, as we accept it during the MSQ 'Moving On'
        // the rest are priority quests, but that's fine here
        if (QuestData.HardModePrimals.Contains(currentQuest.Quest.Id))
            return false;

        if (currentQuest.Quest.Info.AlliedSociety != EAlliedSociety.None)
            return false;

        if (currentQuest.Step > 0)
            return false;

        QuestStep? currentStep = currentQuest.FindStep();
        return currentStep?.AetheryteShortcut != null &&
               (currentStep.SkipConditions?.AetheryteShortcutIf?.QuestsCompleted.Count ?? 0) == 0 &&
               (currentStep.SkipConditions?.AetheryteShortcutIf?.QuestsAccepted.Count ?? 0) == 0;
    }

    // A finished quest only counts once it's no longer accepted (repeatables). After a stop there's no started quest,
    // so an accepted journal quest counts instead.
    internal static bool IsOtherQuestRunning(QuestProgress? started, ElementId priorityQuestId, IQuestSelectionState state,
        Func<ElementId?> journalQuest)
    {
        if (started != null)
            return started.Quest.Id != priorityQuestId &&
                   (!state.IsComplete(started.Quest.Id) || state.IsAccepted(started.Quest.Id));

        return journalQuest() is { } questId && questId != priorityQuestId && state.IsAccepted(questId);
    }

    // The list is only read once nothing else blocks the switch.
    public static ElementId? PickPriorityQuest(QuestSlots slots, bool stopRequested,
        Func<IEnumerable<PriorityQuestInfo>> priorityQuests)
    {
        if (slots.Next != null || slots.Gathering != null || slots.Simulated != null || stopRequested)
            return null;

        ElementId? questId = priorityQuests()
            .Where(x => x.IsAvailable)
            .Select(x => x.QuestId)
            .FirstOrDefault();

        // don't start a second priority quest until the first one is resolved
        if (questId == null || (slots.Started != null && questId == slots.Started.Quest.Id))
            return null;

        return questId;
    }

    private sealed class SimulatedRule : IQuestRule
    {
        public QuestChoice? Offer(QuestSelectionContext context) =>
            context.Slots.Simulated is { } quest ? QuestChoice.ForSlot(EQuestSource.Simulated, quest) : null;
    }

    private sealed class NextRule : IQuestRule
    {
        public QuestChoice? Offer(QuestSelectionContext context) =>
            context.Slots.Next is { } quest && context.State.IsReadyToAccept(quest.Quest.Id)
                ? QuestChoice.ForSlot(EQuestSource.Next, quest)
                : null;
    }

    private sealed class GatheringRule : IQuestRule
    {
        public QuestChoice? Offer(QuestSelectionContext context) =>
            context.Slots.Gathering is { } quest ? QuestChoice.ForSlot(EQuestSource.Gathering, quest) : null;
    }

    // Only the first eligible priority quest is considered; if it can't take over, the journal's quest runs.
    private sealed class PriorityRule : IQuestRule
    {
        public QuestChoice? Offer(QuestSelectionContext context)
        {
            Quest? candidate = context.PriorityQuests
                .Where(x => !context.IsAcceptOnly(x.Id))
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
    QuestReference GetCurrentQuest(bool allowNewMsq);
}

internal sealed record QuestSlots(QuestProgress? Started, QuestProgress? Next, QuestProgress? Gathering, QuestProgress? Simulated);

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
    public QuestReference Current => _current ??= state.GetCurrentQuest(allowNewMsq: automation != EAutomationType.SingleQuestB);
}

internal enum EQuestSource
{
    Simulated,
    Next,
    Gathering,
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
