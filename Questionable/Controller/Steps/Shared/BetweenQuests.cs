using Quest = Questionable.Domain.Quest;
namespace Questionable.Controller.Steps.Shared;

// Detours that can leave the zone (job gear, the Gysahl Greens restock) only start right before a quest is
// accepted: nothing is in progress, and planning the accept step again walks back to the quest giver.
// A step with only a DataId never walks back (MoveTo waits for the object instead).
internal static class BetweenQuests
{
    internal static bool IsAcceptStep(QuestStep step, bool questAccepted) =>
        step is { InteractionType: EInteractionType.AcceptQuest, Position: not null } && !questAccepted;

    internal static bool CanDetour(Quest quest, QuestStep step, QuestFunctions questFunctions, ICondition condition,
        GameFunctions gameFunctions) =>
        IsAcceptStep(step, questFunctions.IsQuestAccepted(quest.Id)) && !IsBusy(condition, gameFunctions);

    private static bool IsBusy(ICondition condition, GameFunctions gameFunctions) =>
        condition[ConditionFlag.InCombat] || condition[ConditionFlag.BoundByDuty] ||
        condition[ConditionFlag.BoundByDuty56] || condition[ConditionFlag.BoundByDuty95] ||
        condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51] ||
        condition[ConditionFlag.OccupiedInCutSceneEvent] || condition[ConditionFlag.WatchingCutscene] ||
        condition[ConditionFlag.Crafting] || condition[ConditionFlag.Gathering] ||
        condition[ConditionFlag.Fishing] || gameFunctions.IsOccupied();
}
