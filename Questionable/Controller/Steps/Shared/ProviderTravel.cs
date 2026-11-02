using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Shared;

/// <summary>
///     Gather n Chill and Feesh switch job and travel themselves, so Questionable skips its own travel for their steps
///     and for walk-only steps that just lead to them.
/// </summary>
internal static class ProviderTravel
{
    public static bool IsHandled(QuestSequence sequence, QuestStep step, IFeeshIpc feeshIpc)
    {
        if (IsProviderStep(step, feeshIpc))
            return true;
        if (!IsWalkOnly(step))
            return false;

        int index = sequence.Steps.IndexOf(step);
        QuestStep? next = index < 0 ? null : sequence.Steps.Skip(index + 1).FirstOrDefault(x => !IsWalkOnly(x));
        return next != null && IsProviderStep(next, feeshIpc);
    }

    // Gathering always goes through Gather n Chill; steps naming a quest-specific node (e.g. 3833) still walk there.
    // Fish Feesh can't catch keep their travel for manual fishing.
    private static bool IsProviderStep(QuestStep step, IFeeshIpc feeshIpc) =>
        step is { InteractionType: EInteractionType.Gather, DataId: null } || Fish.IsHandledByFeesh(step, feeshIpc);

    private static bool IsWalkOnly(QuestStep step) =>
        step.InteractionType is EInteractionType.WalkTo or EInteractionType.None &&
        !QuestWorkUtils.HasCompletionFlags(step.CompletionQuestVariablesFlags);
}
