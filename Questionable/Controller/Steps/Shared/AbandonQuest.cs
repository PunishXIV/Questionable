using System.Globalization;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Extensions;
using Questionable.Functions;

namespace Questionable.Controller.Steps.Shared;

internal static class AbandonQuest
{

    internal sealed record Task(Quest? Quest) : ITask
    {
        public override string ToString() => $"AbandonQuest({Quest?.Id.Value.ToString(CultureInfo.InvariantCulture) ?? "???"})";
    }

    internal sealed class AbandonQuestExecutor
    (
        IClientState clientState,
        IObjectTable objectTable,
        ICondition condition,
        IChatGui chatGui,
        Configuration configuration,
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        QuestController questController,
        ILogger<AbandonQuestExecutor> logger) : TaskExecutor<Task>
    {
        private uint attempts;
        protected override bool Start()
        {
            // Safety check: ensure player is logged in
            if (objectTable[0] == null || !clientState.IsLoggedIn || Task.Quest == null)
            {
                throw new TaskException("Player is not logged in or quest is null");
            }

            if (condition[ConditionFlag.InCombat] ||
                condition[ConditionFlag.Unconscious] ||
                condition[ConditionFlag.BoundByDuty] ||
                condition[ConditionFlag.InDeepDungeon] ||
                condition[ConditionFlag.WatchingCutscene] ||
                condition[ConditionFlag.WatchingCutscene78] ||
                condition[ConditionFlag.BetweenAreas] ||
                condition[ConditionFlag.BetweenAreas51] ||
                gameFunctions.IsOccupied())
            {
                throw new TaskException("Player is busy");
            }

            if (!((QuestInfo)Task.Quest.Info).CanCancel)
            {
                throw new TaskException("Quest cannot be cancelled");
            }

            AbandonQuestAction();
            return true;
        }

        public override ETaskResult Update()
        {
            if (Task.Quest == null || !questFunctions.IsQuestAccepted(Task.Quest.Id))
            {
                logger.LogInformation("Quest abandoned");
                chatGui.Print($"Quest abandoned{(Task.Quest != null ? $": {Task.Quest?.Info.Name}" : "")}");
                return ETaskResult.TaskComplete;
            }
            if (EzThrottler.Throttle("AbandonQuest")) AbandonQuestAction();
            return ETaskResult.StillRunning;
        }

        public void AbandonQuestAction()
        {
            if (attempts >= 5)
            {
                configuration.Advanced.AbandonQuestBeforeCompletion = false;
                configuration.Save();
                throw new TaskException("AbandonQuest failed, disabling config option and stopping automatic questing.");
            }
            logger.LogInformation($"Firing AbandonQuest for {Task.Quest?.Id.Value}");
            GameMain.ExecuteCommand((int)GameCommand.AbandonQuest, (int)Task.Quest!.Id.Value);
            attempts += 1;
            if (configuration.Advanced.RemoveFromPriorityWhenAbandoned)
                questController.PriorityManager.Remove(Task.Quest.Id);
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
