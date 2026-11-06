using Questionable.Model.Questing;
using Quest = Questionable.Domain.Quest;
using EAutomationType = Questionable.Controller.QuestController.EAutomationType;
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
    IQuestSelectionState state)
{
    private QuestReference? _current;

    public QuestSlots Slots => slots;
    public IReadOnlyList<Quest> PriorityQuests => priorityQuests;
    public Func<ElementId, bool> IsAcceptOnly => isAcceptOnly;
    public IQuestSelectionState State => state;
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
