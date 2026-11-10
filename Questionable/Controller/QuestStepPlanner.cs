using ECurrentQuestType = Questionable.Controller.QuestController.ECurrentQuestType;
using QuestProgress = Questionable.Controller.QuestController.QuestProgress;

namespace Questionable.Controller;

// Decides what the current quest step means and where it goes next; QuestController applies the result.
internal static class QuestStepPlanner
{
    public const int CompletedStep = 255;

    public static EStepState CheckStep(QuestProgress progress)
    {
        QuestSequence? sequence = progress.FindSequence();
        if (sequence == null)
            return EStepState.UnknownSequence;
        if (progress.Step == CompletedStep)
            return EStepState.Completed;
        if (sequence.Steps.Count > 0 && progress.Step >= sequence.Steps.Count)
            return EStepState.UnknownStep;
        return EStepState.Valid;
    }

    public static StepAdvance Advance(QuestProgress? current, QuestSequence? sequence, QuestStep? step,
        ElementId? questId, int? expectedSequence)
    {
        if (current == null || sequence == null || step == null)
            return new(EStepAdvance.NoStep);
        if (questId != null && current.Quest.Id != questId)
            return new(EStepAdvance.OtherQuest);
        if (expectedSequence != null && sequence.Sequence != expectedSequence.Value)
            return new(EStepAdvance.OtherSequence);
        return new(EStepAdvance.Advanced, current.Step + 1 < sequence.Steps.Count ? current.Step + 1 : CompletedStep);
    }

    public static bool IsGatheringDeliveryDone((QuestProgress Progress, ECurrentQuestType Type)? details) =>
        details is { Progress: { Quest.Id: SatisfactionSupplyNpcId, Sequence: 1, Step: CompletedStep }, Type: ECurrentQuestType.Gathering };
}

internal enum EStepState
{
    Valid,
    UnknownSequence,
    Completed,
    UnknownStep,
}

internal enum EStepAdvance
{
    NoStep,
    OtherQuest,
    OtherSequence,
    Advanced,
}

internal sealed record StepAdvance(EStepAdvance Kind, int NextStep = 0);
