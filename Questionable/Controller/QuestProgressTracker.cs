using Microsoft.Extensions.Logging;
using Questionable.Controller.Utils;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Quest = Questionable.Model.Quest;

namespace Questionable.Controller;

/// <summary>
///     Tracks which quest is currently being worked on. There are up to five quest "slots"
///     (started / simulated / next / gathering / pending); this class owns those slots, the rules
///     that pick the currently-active one, and the setters that mutate them. Extracted from
///     <see cref="QuestController"/>.
/// </summary>
internal sealed class QuestProgressTracker(
    QuestFunctions questFunctions,
    QuestRegistry questRegistry,
    HighlightObject highlightObject,
    ILogger<QuestProgressTracker> logger)
{
    public QuestController.QuestProgress? StartedQuest { get; set; }
    public QuestController.QuestProgress? SimulatedQuest { get; set; }
    public QuestController.QuestProgress? NextQuest { get; set; }
    public QuestController.QuestProgress? GatheringQuest { get; set; }

    /// <summary>
    ///     Used when accepting leves, as there's a small delay.
    /// </summary>
    public QuestController.QuestProgress? PendingQuest { get; set; }

    public (QuestController.QuestProgress Progress, QuestController.ECurrentQuestType Type)? CurrentQuestDetails
    {
        get
        {
            if (SimulatedQuest != null)
                return (SimulatedQuest, QuestController.ECurrentQuestType.Simulated);
            if (NextQuest != null && questFunctions.IsReadyToAcceptQuest(NextQuest.Quest.Id))
                return (NextQuest, QuestController.ECurrentQuestType.Next);
            if (GatheringQuest != null)
                return (GatheringQuest, QuestController.ECurrentQuestType.Gathering);
            if (StartedQuest != null)
                return (StartedQuest, QuestController.ECurrentQuestType.Normal);

            return null;
        }
    }

    public QuestController.QuestProgress? CurrentQuest => CurrentQuestDetails?.Progress;

    /// <summary>Clears every quest slot.</summary>
    public void Reset()
    {
        StartedQuest = null;
        NextQuest = null;
        GatheringQuest = null;
        PendingQuest = null;
        SimulatedQuest = null;
    }

    public void SimulateQuest(IQuestInfo? questInfo, byte sequence, int step)
    {
        if (questInfo is not null && questRegistry.TryGetQuest(questInfo.QuestId, out Quest? quest))
            SimulateQuest(quest, sequence, step);
    }

    public void SimulateQuest(Quest? quest, byte sequence, int step)
    {
        highlightObject.SetHighlight([]);
        logger.LogInformation("SimulateQuest: {QuestId}", quest?.Id);
        if (quest != null)
            SimulatedQuest = new(quest, sequence, step);
        else
            SimulatedQuest = null;
    }

    public void StopSimulate()
    {
        highlightObject.SetHighlight([]);
        logger.LogInformation("StopSimulate");
        SimulatedQuest = null;
    }

    public void SetNextQuest(Quest? quest)
    {
        highlightObject.SetHighlight([]);
        logger.LogInformation("NextQuest: {QuestId}", quest?.Id);
        if (quest != null)
            NextQuest = new(quest);
        else
            NextQuest = null;
    }

    public void SetGatheringQuest(Quest? quest)
    {
        highlightObject.SetHighlight([]);
        logger.LogInformation("GatheringQuest: {QuestId}", quest?.Id);
        if (quest != null)
            GatheringQuest = new(quest);
        else
            GatheringQuest = null;
    }

    public void SetPendingQuest(QuestController.QuestProgress? quest)
    {
        highlightObject.SetHighlight([]);
        logger.LogInformation("PendingQuest: {QuestId}", quest?.Quest.Id);
        PendingQuest = quest;
    }

    public (QuestSequence? Sequence, QuestStep? Step, bool createTasks) GetNextStep()
    {
        if (CurrentQuest == null)
            return (null, null, false);

        Quest q = CurrentQuest.Quest;
        QuestSequence? seq = q.FindSequence(CurrentQuest.Sequence);
        if (seq == null)
            return (null, null, true);

        if (seq.Steps.Count == 0)
            return (seq, null, true);

        if (CurrentQuest.Step >= seq.Steps.Count)
            return (null, null, false);

        return (seq, seq.Steps[CurrentQuest.Step], true);
    }
}
