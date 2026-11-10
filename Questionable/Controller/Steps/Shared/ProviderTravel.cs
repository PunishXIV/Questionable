namespace Questionable.Controller.Steps.Shared;

// Providers that travel themselves take over the related travel steps.
internal static class ProviderTravel
{
    public static bool IsHandled(QuestSequence sequence, QuestStep step, IEnumerable<IOperationProvider> providers)
    {
        IOperationProvider[] all = providers as IOperationProvider[] ?? providers.ToArray();
        if (all.Any(x => x.HandlesTravel(step)))
            return true;
        if (!IsWalkOnly(step))
            return false;

        int index = sequence.Steps.IndexOf(step);
        QuestStep? next = index < 0 ? null : sequence.Steps.Skip(index + 1).FirstOrDefault(x => !IsWalkOnly(x));
        return next != null && all.Any(x => x.HandlesLeadInTravel && x.HandlesTravel(next));
    }

    private static bool IsWalkOnly(QuestStep step) =>
        step.InteractionType is EInteractionType.WalkTo or EInteractionType.None &&
        !QuestWorkUtils.HasCompletionFlags(step.CompletionQuestVariablesFlags);
}
