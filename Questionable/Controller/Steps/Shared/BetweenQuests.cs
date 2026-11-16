using Quest = Questionable.Domain.Quest;
namespace Questionable.Controller.Steps.Shared;

// Detours that can leave the zone (job gear, reward mail, the Gysahl Greens restock) only start right before a quest is
// accepted: nothing is in progress, and planning the accept step again walks back to the quest giver.
// A step with only a DataId never walks back (MoveTo waits for the object instead).
internal static class BetweenQuests
{
    internal static bool IsAcceptStep(QuestStep step, bool questAccepted) =>
        step is { InteractionType: EInteractionType.AcceptQuest, Position: not null } && !questAccepted;

    internal static bool CanDetour(Quest quest, QuestStep step, QuestFunctions questFunctions, ICondition condition,
        GameFunctions gameFunctions, QuestPriorityManager priorityManager) =>
        !priorityManager.IsDailiesRun && IsAcceptStep(step, questFunctions.IsQuestAccepted(quest.Id)) && !IsBusy(condition, gameFunctions) &&
        !IsAcceptAllUnderway([.. priorityManager.AcceptOnlyQuests.Select(x => x.Id)], questFunctions.IsQuestAccepted,
            x => questFunctions.IsReadyToAcceptQuest(x, ignoreLevel: true));

    // Once Accept All has taken a quest, the rest wait at the same givers; leaving now means coming back.
    internal static bool IsAcceptAllUnderway(IReadOnlyList<ElementId> acceptOnly, Func<ElementId, bool> isAccepted,
        Func<ElementId, bool> isReady) =>
        acceptOnly.Any(isAccepted) && acceptOnly.Any(isReady);

    private static bool IsBusy(ICondition condition, GameFunctions gameFunctions) =>
        condition[ConditionFlag.InCombat] || condition[ConditionFlag.BoundByDuty] ||
        condition[ConditionFlag.BoundByDuty56] || condition[ConditionFlag.BoundByDuty95] ||
        condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51] ||
        condition[ConditionFlag.OccupiedInCutSceneEvent] || condition[ConditionFlag.WatchingCutscene] ||
        condition[ConditionFlag.Crafting] || condition[ConditionFlag.Gathering] ||
        condition[ConditionFlag.Fishing] || gameFunctions.IsOccupied();
}
