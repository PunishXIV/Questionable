using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Shared;

internal static class CraftingBatch
{
    internal static IReadOnlyList<CraftingItem>? Resolve(Craft.CraftTask task, IReadOnlySet<uint>? requiredItemIds)
    {
        // Keep legacy list-only compatibility, including an empty, unverified target set.
        if (task.ItemId == null && task.ItemCount == null)
            return Craft.GetItemTargets(task);

        if (task.Quest.Id is not QuestId || task.Quest.Root.Disabled || requiredItemIds is not { Count: > 0 })
            return null;

        var crafts = task.Quest.AllSteps().Where(x => x.Step.InteractionType == EInteractionType.Craft).ToList();
        if (crafts.Count == 0 || !crafts.Any(x => ReferenceEquals(x.Step, task.Step)))
            return null;

        QuestSequence sequence = crafts[0].Sequence;
        if (sequence.Sequence != task.Sequence ||
            crafts.Any(x => !ReferenceEquals(x.Sequence, sequence) || HasUncertainScope(x.Step)) ||
            crafts[^1].StepId - crafts[0].StepId + 1 != crafts.Count)
            return null;

        IReadOnlyList<CraftingItem> requirements = QuestCraftingRequirements.GetQuestItems(task.Quest.Id);
        if (requirements.Count == 0 || !requiredItemIds.SetEquals(requirements.Select(x => x.ItemId)))
            return null;

        var outputs = new Dictionary<uint, CraftingItem>();
        foreach (var craft in crafts)
        {
            // Subcrafts can be consumed by the list; only final outputs belong in its objective.
            if (!requiredItemIds.Contains(craft.Step.ItemId!.Value))
                continue;

            IReadOnlyList<CraftingItem> targets = Craft.GetItemTargets(new Craft.CraftTask(task.Quest, task.Sequence, craft.Step));
            if (targets.Count != 1 || targets[0].ItemId != craft.Step.ItemId.Value)
                return null;

            // Metadata correction must not hide an explicitly incompatible NQ/HQ requirement.
            if (craft.Step.ItemQuality != EItemQuality.Any && targets[0].Quality != EItemQuality.Any &&
                craft.Step.ItemQuality != targets[0].Quality)
                return null;
            if (!TryMerge(outputs, targets[0]))
                return null;
        }

        // Every native output must be explicitly represented in this block, with complete metadata.
        if (!requiredItemIds.SetEquals(outputs.Keys))
            return null;
        foreach (CraftingItem requirement in requirements)
        {
            if (!TryMerge(outputs, requirement))
                return null;
        }

        return outputs.Values.ToArray();
    }

    private static bool TryMerge(Dictionary<uint, CraftingItem> outputs, CraftingItem item)
    {
        if (item.ItemId == 0 || item.ItemCount <= 0 ||
            item.Quality is not (EItemQuality.Any or EItemQuality.NQ or EItemQuality.HQ))
            return false;

        if (!outputs.TryGetValue(item.ItemId, out CraftingItem? previous))
        {
            outputs.Add(item.ItemId, item);
            return true;
        }

        if (previous.Quality != item.Quality && previous.Quality != EItemQuality.Any && item.Quality != EItemQuality.Any)
            return false;

        // A larger Any threshold must not increase how many NQ/HQ items are required.
        if (previous.Quality != item.Quality &&
            ((previous.Quality == EItemQuality.Any && previous.ItemCount > item.ItemCount) ||
             (item.Quality == EItemQuality.Any && item.ItemCount > previous.ItemCount)))
            return false;

        // These are inventory thresholds, not quantities to craft again for each path step.
        outputs[item.ItemId] = new CraftingItem(item.ItemId, Math.Max(previous.ItemCount, item.ItemCount),
            previous.Quality == EItemQuality.Any ? item.Quality : previous.Quality);
        return true;
    }

    private static bool HasUncertainScope(QuestStep step)
    {
        if (step.Disabled || step.ItemId is null or 0 || step.ItemCount is null or <= 0 ||
            step.ItemQuality is not (EItemQuality.Any or EItemQuality.NQ or EItemQuality.HQ) ||
            step.RequiredQuestVariables.Any(x => x is { Count: > 0 }) ||
            step.CompletionQuestVariablesFlags.Any(x => x != null) ||
            step.RequiredCurrentJob.Count > 0 || step.RequiredQuestAcceptedJob.Count > 0 ||
            step.PickUpQuestId != null || step.TurnInQuestId != null || step.NextQuestId != null || step.TaxiStandId != null)
            return true;

        SkipStepConditions? skip = step.SkipConditions?.StepIf;
        if (skip == null || skip.Never)
            return false;

        // Inventory sufficiency does not change the batch's scope. Other skip predicates can.
        return skip.Item is { NotInInventory: true } ||
               skip.CompletionQuestVariablesFlags.Any(x => x != null) ||
               skip.Flying != null || skip.Chocobo != null || skip.Diving != null || skip.NotTargetable ||
               skip.InTerritory.Count > 0 || skip.NotInTerritory.Count > 0 ||
               skip.QuestsAccepted.Count > 0 || skip.QuestsCompleted.Count > 0 || skip.NotNamePlateIconId.Count > 0 ||
               skip.AetheryteLocked != null || skip.AetheryteUnlocked != null ||
               skip.NearPosition != null || skip.NotNearPosition != null ||
               skip.ExtraCondition is not (null or EExtraSkipCondition.None) ||
               skip.DutiesUnlocked.Count > 0 || skip.DutiesCompleted.Count > 0;
    }
}
