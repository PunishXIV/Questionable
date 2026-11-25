using Quest = Questionable.Domain.Quest;
namespace Questionable.Controller.Steps.Shared;

// Zone-leaving detours only start right before an accept, whose replanned step walks back to the giver.
internal static class BetweenQuests
{
    internal static bool IsAcceptStep(QuestStep step, bool questAccepted) =>
        step is { InteractionType: EInteractionType.AcceptQuest, Position: not null } && !questAccepted;

    internal static bool CanDetour(Quest quest, QuestStep step, QuestFunctions questFunctions, ICondition condition,
        GameFunctions gameFunctions, QuestPriorityManager priorityManager) =>
        !priorityManager.IsManualRun && !priorityManager.IsDailiesRun && IsAcceptStep(step, questFunctions.IsQuestAccepted(quest.Id)) && !IsBusy(condition, gameFunctions) &&
        !IsAcceptAllUnderway([.. priorityManager.AcceptOnlyQuests.Select(x => x.Id)], questFunctions.IsQuestAccepted,
            x => questFunctions.IsReadyToAcceptQuest(x, ignoreLevel: true));

    internal static bool IsAcceptAllUnderway(IReadOnlyList<ElementId> acceptOnly, Func<ElementId, bool> isAccepted,
        Func<ElementId, bool> isReady) =>
        acceptOnly.Any(isAccepted) && acceptOnly.Any(isReady);

    // No reset on success: a detour that fails on and off still fades out over a long session.
    internal sealed class Backoff
    {
        private int _failures;
        private DateTime _until = DateTime.MinValue;

        public bool Waiting => DateTime.Now < _until;

        public void Failed() => _until = DateTime.Now + Delay(_failures++);

        internal static TimeSpan Delay(int failures) => TimeSpan.FromMinutes(30 * (1 << Math.Min(failures, 4)));
    }

    private static bool IsBusy(ICondition condition, GameFunctions gameFunctions) =>
        condition[ConditionFlag.InCombat] || condition[ConditionFlag.BoundByDuty] ||
        condition[ConditionFlag.BoundByDuty56] || condition[ConditionFlag.BoundByDuty95] ||
        condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51] ||
        condition[ConditionFlag.OccupiedInCutSceneEvent] || condition[ConditionFlag.WatchingCutscene] ||
        condition[ConditionFlag.Crafting] || condition[ConditionFlag.Gathering] ||
        condition[ConditionFlag.Fishing] || gameFunctions.IsOccupied();
}
