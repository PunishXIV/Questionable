using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.GameData;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.External;
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
                return [];

            ArgumentNullException.ThrowIfNull(step.ItemId);
            ArgumentNullException.ThrowIfNull(step.ItemCount);
            return
            [
                new Mount.UnmountTask(),
                new CraftTask(step.ItemId.Value, step.ItemCount.Value)
            ];
        }
    }

    internal sealed record CraftTask(
        uint ItemId,
        int ItemCount) : ITask
    {
        public override string ToString() => $"Craft {ItemCount}x {ItemId} (with Artisan)";
    }

    internal sealed class DoCraft(
        IDataManager dataManager,
        IClientState clientState,
        ArtisanIpc artisanIpc,
        ILogger<DoCraft> logger) : TaskExecutor<CraftTask>
    {
        protected override bool Start()
        {
            if (HasRequestedItems())
            {
                logger.LogInformation("Already own {ItemCount}x {ItemId}", Task.ItemCount, Task.ItemId);
                return false;
            }

            RecipeLookup? recipeLookup = dataManager.GetExcelSheet<RecipeLookup>().GetRowOrDefault(Task.ItemId);
            if (recipeLookup == null)
                throw new TaskException($"Item {Task.ItemId} is not craftable");

            uint recipeId = (EClassJob)clientState.LocalPlayer!.ClassJob.RowId switch
            {
                EClassJob.Carpenter => recipeLookup.Value.CRP.RowId,
                EClassJob.Blacksmith => recipeLookup.Value.BSM.RowId,
                EClassJob.Armorer => recipeLookup.Value.ARM.RowId,
                EClassJob.Goldsmith => recipeLookup.Value.GSM.RowId,
                EClassJob.Leatherworker => recipeLookup.Value.LTW.RowId,
                EClassJob.Weaver => recipeLookup.Value.WVR.RowId,
                EClassJob.Alchemist => recipeLookup.Value.ALC.RowId,
                EClassJob.Culinarian => recipeLookup.Value.CUL.RowId,
                _ => 0
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
                throw new TaskException($"Unable to determine recipe for item {Task.ItemId}");

            int remainingItemCount = Task.ItemCount - GetOwnedItemCount();
            logger.LogInformation(
                "Starting craft for item {ItemId} with recipe {RecipeId} for {RemainingItemCount} items",
                Task.ItemId, recipeId, remainingItemCount);
            if (!artisanIpc.CraftItem((ushort)recipeId, remainingItemCount))
                throw new TaskException($"Failed to start Artisan craft for recipe {recipeId}");

            return true;
        }

        public override unsafe ETaskResult Update()
        {
            if (HasRequestedItems() && !artisanIpc.IsCrafting())
            {
                AgentRecipeNote* agentRecipeNote = AgentRecipeNote.Instance();
                if (agentRecipeNote != null && agentRecipeNote->IsAgentActive())
                {
                    uint addonId = agentRecipeNote->GetAddonId();
                    if (addonId == 0)
                        return ETaskResult.StillRunning;

                    AtkUnitBase* addon = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonById((ushort)addonId);
                    if (addon != null)
                    {
                        logger.LogInformation("Closing crafting window");
                        addon->FireCallbackInt(-1);
                        return ETaskResult.TaskComplete;
                    }
                }
            }

            return ETaskResult.StillRunning;
        }

        private bool HasRequestedItems() => GetOwnedItemCount() >= Task.ItemCount;

        private unsafe int GetOwnedItemCount()
        {
            InventoryManager* inventoryManager = InventoryManager.Instance();
            return inventoryManager->GetInventoryItemCount(Task.ItemId, isHq: false, checkEquipped: false)
                   + inventoryManager->GetInventoryItemCount(Task.ItemId, isHq: true, checkEquipped: false);
        }

        // we're on a crafting class, so combat doesn't make much sense (we also can't change classes in combat...)
        public override bool ShouldInterruptOnDamage() => false;
    }
}
