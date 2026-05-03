using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.GameData;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.External;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Linq;
using Mount = Questionable.Controller.Steps.Common.Mount;
using Quest = Questionable.Model.Quest;

namespace Questionable.Controller.Steps.Shared;

internal static class Craft
{
    internal sealed class Factory : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Craft)
            {
                return [];
            }

            ArgumentNullException.ThrowIfNull(step.ItemId);
            ArgumentNullException.ThrowIfNull(step.ItemCount);
            return
            [
                new Mount.UnmountTask(),
                new CraftTask(quest, step.ItemId.Value, step.ItemCount.Value)
            ];
        }
    }

    internal sealed record CraftTask
    (
        Quest Quest,
        uint ItemId,
        int ItemCount) : ITask
    {
        public override string ToString()
        {
            return $"Craft({ItemCount}x {ItemId})";
        }
    }

    internal sealed class DoCraft
    (
        IDataManager dataManager,
        QuestFunctions questFunctions,
        ArtisanIpc artisanIpc,
        ILogger<DoCraft> logger,
        QuestController questController) : TaskExecutor<CraftTask>
    {
        private EItemQuality _itemQuality = EItemQuality.Any;
        private int _previousCount;
        private int _startingItemCount;
        protected override unsafe bool Start()
        {
            // Get the item quality requirement from the quest step (NQ, HQ, or Any)
            _itemQuality = GetItemQuality();
            int ownedCount = GetOwnedItemCount();

            if (ownedCount >= Task.ItemCount)
            {
                logger.LogInformation("Already own {ItemCount}x {ItemId}", Task.ItemCount, Task.ItemId);
                return false;
            }

            _startingItemCount = ownedCount;
            _previousCount = ownedCount;

            RecipeLookup? recipeLookup = dataManager.GetExcelSheet<RecipeLookup>().GetRowOrDefault(Task.ItemId) ??
                                         throw new TaskException($"Item {Task.ItemId} is not craftable");
            QuestProgressInfo? questWork = questFunctions.GetQuestProgressInfo(Task.Quest.Id);
            uint recipeId = (questWork != null && questWork.ClassJob.IsCrafter() ?
                    questWork.ClassJob :
                    (EClassJob)PlayerState.Instance()->CurrentClassJobId
                ) switch
                {
                    EClassJob.Carpenter => recipeLookup.Value.CRP.RowId,
                    EClassJob.Blacksmith => recipeLookup.Value.BSM.RowId,
                    EClassJob.Armorer => recipeLookup.Value.ARM.RowId,
                    EClassJob.Goldsmith => recipeLookup.Value.GSM.RowId,
                    EClassJob.Leatherworker => recipeLookup.Value.LTW.RowId,
                    EClassJob.Weaver => recipeLookup.Value.WVR.RowId,
                    EClassJob.Alchemist => recipeLookup.Value.ALC.RowId,
                    EClassJob.Culinarian => recipeLookup.Value.CUL.RowId,
                    var _ => 0
                };

            if (recipeId == 0)
            {
                recipeId = new[]
                    {
                        recipeLookup.Value.CRP.RowId,
                        recipeLookup.Value.BSM.RowId,
                        recipeLookup.Value.ARM.RowId,
                        recipeLookup.Value.GSM.RowId,
                        recipeLookup.Value.LTW.RowId,
                        recipeLookup.Value.WVR.RowId,
                        recipeLookup.Value.ALC.RowId,
                        recipeLookup.Value.WVR.RowId
                    }
                    .FirstOrDefault(x => x != 0);
            }

            if (recipeId == 0)
            {
                throw new TaskException($"Unable to determine recipe for item {Task.ItemId}");
            }

            int remainingItemCount = Task.ItemCount - _startingItemCount;
            logger.LogInformation(
                "Starting craft for item {ItemId} with recipe {RecipeId} for {RemainingItemCount} items (quality: {Quality}, owned: {OwnedCount})",
                Task.ItemId, recipeId, remainingItemCount, _itemQuality, _startingItemCount);
            if (!artisanIpc.CraftItem((ushort)recipeId, remainingItemCount))
            {
                throw new TaskException($"Failed to start Artisan craft for recipe {recipeId}");
            }

            return true;
        }

        public override unsafe ETaskResult Update()
        {
            int currentCount = GetOwnedItemCount();

            // Log only when item count changes
            if (currentCount != _previousCount)
            {
                int craftedCount = currentCount - _startingItemCount;
                logger.LogInformation("Craft progress: {Current}/{Target} items (crafted: {Crafted}, quality: {Quality})",
                    currentCount, Task.ItemCount, craftedCount, _itemQuality);
                _previousCount = currentCount;
            }

            // Check if we've reached the target count and crafting has stopped
            if (currentCount >= Task.ItemCount && !artisanIpc.IsCrafting())
            {
                logger.LogInformation("Item count reached ({Count}x {ItemId}), closing crafting window", Task.ItemCount, Task.ItemId);
                AgentRecipeNote* agentRecipeNote = AgentRecipeNote.Instance();
                if (agentRecipeNote != null && agentRecipeNote->IsAgentActive())
                {
                    uint addonId = agentRecipeNote->GetAddonId();
                    if (addonId == 0)
                    {
                        return ETaskResult.StillRunning;
                    }

                    AtkUnitBase* addon = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonById((ushort)addonId);
                    if (addon != null)
                    {
                        addon->FireCallbackInt(-1);
                        return ETaskResult.TaskComplete;
                    }
                }
                return ETaskResult.TaskComplete;
            }

            return ETaskResult.StillRunning;
        }

        private EItemQuality GetItemQuality()
        {
            // Retrieve ItemQuality from the current quest step, defaults to Any if not specified
            if (questController.CurrentQuest is { } currentQuest)
            {
                QuestSequence? sequence = currentQuest.Quest.FindSequence(currentQuest.Sequence);
                if (sequence?.Steps.Count > currentQuest.Step)
                {
                    return sequence.Steps[currentQuest.Step].ItemQuality;
                }
            }
            return EItemQuality.Any;
        }

        private unsafe int GetOwnedItemCount()
        {
            // Count items in inventory based on quality requirement: NQ, HQ, or both (Any)
            InventoryManager* inventoryManager = InventoryManager.Instance();
            return _itemQuality switch
            {
                EItemQuality.NQ => inventoryManager->GetInventoryItemCount(Task.ItemId, false, false),
                EItemQuality.HQ => inventoryManager->GetInventoryItemCount(Task.ItemId, true, false),
                EItemQuality.Any => inventoryManager->GetInventoryItemCount(Task.ItemId, false, false)
                                    + inventoryManager->GetInventoryItemCount(Task.ItemId, true, false),
                var _ => 0
            };
        }

        // we're on a crafting class, so combat doesn't make much sense (we also can't change classes in combat...)
        public override bool ShouldInterruptOnDamage()
        {
            return false;
        }
    }
}
