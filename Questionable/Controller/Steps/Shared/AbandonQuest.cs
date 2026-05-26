using System.Globalization;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Utils;

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
        GameFunctions gameFunctions,
        QuestFunctions questFunctions,
        QuestController questController,
        ILogger<AbandonQuestExecutor> logger) : AbstractDelayedTaskExecutor<Task>
    {
        protected override unsafe bool StartInternal()
        {
            // Safety check: ensure player is logged in
            if (objectTable[0] == null || !clientState.IsLoggedIn || Task.Quest == null)
            {
                throw new TaskException(logger.LogChatError(chatGui, "Cannot abandon quest", "Player is not logged in or quest is null"));
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
                throw new TaskException(logger.LogChatError(chatGui, "Cannot abandon quest", "Player is busy"));
            }

            if (!((QuestInfo)Task.Quest.Info).CanCancel)
            {
                throw new TaskException(logger.LogChatError(chatGui, "Cannot abandon quest", "Quest cannot be cancelled"));
            }
            
            AbandonQuestAction();
            return true;
        }

        protected override ETaskResult UpdateInternal()
        {
            if (Task.Quest == null || !questFunctions.IsQuestAccepted(Task.Quest.Id))
            {
                logger.LogChat(chatGui, "Quest abandoned");
                return ETaskResult.TaskComplete;
            }
            AbandonQuestAction();
            return ETaskResult.StillRunning;
        }

        public void AbandonQuestAction()
        {
            logger.LogInformation($"Firing AbandonQuest for {Task.Quest?.Id.Value}");
            GameMain.ExecuteCommand(800, (int)Task.Quest!.Id.Value);
            questController.PriorityManager.Remove(Task.Quest);
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
