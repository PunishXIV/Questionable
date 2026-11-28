using QuestProgress = Questionable.Controller.QuestController.QuestProgress;

namespace Questionable.Controller;

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

    // A sim never gets its sequence from the game, so past a sequence's last step it goes to the next one.
    public static (byte Sequence, int Step)? NextStep(QuestProgress progress)
    {
        if (progress.FindSequence() is { } sequence && progress.Step + 1 < sequence.Steps.Count)
            return (progress.Sequence, progress.Step + 1);

        QuestSequence? next = progress.Quest.Root.QuestSequence
            .Where(x => x.Sequence > progress.Sequence)
            .MinBy(x => x.Sequence);
        return next == null ? null : (next.Sequence, 0);
    }
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
