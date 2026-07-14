using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.Interop;
using Questionable.Domain;
using Questionable.External;
using Questionable.Model.Common;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Interactions;

internal static class EquipRecommended
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.EquipRecommended)
                return null;

            return new EquipTask();
        }
    }

    internal sealed class BeforeDutyOrInstance : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Duty &&
                step.InteractionType != EInteractionType.SinglePlayerDuty &&
                step.InteractionType != EInteractionType.Combat)
                return null;

            return new EquipTask();
        }
    }

    internal sealed class EquipTask : ITask
    {
        public override string ToString() => "EquipRecommended";
    }

    internal sealed unsafe class DoEquipRecommended(IChatGui chatGui, ICondition condition, Configuration config, StylistIpc stylist)
        : TaskExecutor<EquipTask>
    {
        private bool _checkedOrTriggeredEquipmentUpdate;
        private DateTime _continueAt = DateTime.MinValue;

        protected override bool Start()
        {
            if (condition[ConditionFlag.InCombat])
                return false;

            if (!StylistIpc.IsInstalled && config.General.GearsetUpdateSource is EGearsetUpdateSource.Stylist)
            {
                chatGui.Print("You've set Stylist to manage equipped gear, but it is not installed. Resetting to Vanilla.", CommandHandler.MessageTag, CommandHandler.TagColor);
                config.General.GearsetUpdateSource = EGearsetUpdateSource.Vanilla;
                Svc.PluginInterface.SavePluginConfig(config);
            }
            switch (config.General.GearsetUpdateSource)
            {
                case EGearsetUpdateSource.Vanilla:
                    RecommendEquipModule.Instance()->SetupForClassJob(PlayerState.Instance()->CurrentClassJobId);
                    break;
                case EGearsetUpdateSource.Stylist:
                    RaptureGearsetModule.Instance()->UpdateGearset(RaptureGearsetModule.Instance()->CurrentGearsetIndex);
                    break;
            }

            return true;
        }

        public override ETaskResult Update()
        {
            switch (config.General.GearsetUpdateSource)
            {
                case EGearsetUpdateSource.Vanilla:
                    RecommendEquipModule* recommendedEquipModule = RecommendEquipModule.Instance();
                    if (recommendedEquipModule->IsUpdating)
                        return ETaskResult.StillRunning;

                    if (!_checkedOrTriggeredEquipmentUpdate)
                    {
                        if (!IsAllRecommendedGearEquipped())
                        {
                            chatGui.Print("Equipping recommended gear.", CommandHandler.MessageTag, CommandHandler.TagColor);
                            recommendedEquipModule->EquipRecommendedGear();
                            _continueAt = DateTime.Now.AddSeconds(1);
                        }

                        _checkedOrTriggeredEquipmentUpdate = true;
                        return ETaskResult.StillRunning;
                    }

                    break;
                case EGearsetUpdateSource.Stylist:
                    {
                        if (stylist.IsBusy)
                            return ETaskResult.StillRunning;

                        if (!_checkedOrTriggeredEquipmentUpdate)
                        {
                            stylist.UpdateGearset();
                            _checkedOrTriggeredEquipmentUpdate = true;
                            _continueAt = DateTime.Now.AddSeconds(1);
                            return ETaskResult.StillRunning;
                        }
                    }

                    break;
            }

            return DateTime.Now >= _continueAt ? ETaskResult.TaskComplete : ETaskResult.StillRunning;
        }

        private bool IsAllRecommendedGearEquipped()
        {
            RecommendEquipModule* recommendedEquipModule = RecommendEquipModule.Instance();
            InventoryManager* inventoryManager = InventoryManager.Instance();
            InventoryContainer* equippedItems =
                inventoryManager->GetInventoryContainer(InventoryType.EquippedItems);
            foreach (Pointer<InventoryItem> recommendedItemPtr in recommendedEquipModule->RecommendedItems)
            {
                InventoryItem* recommendedItem = recommendedItemPtr.Value;
                if (recommendedItem == null || recommendedItem->ItemId == 0)
                    continue;

                bool isEquipped = false;
                for (int i = 0; i < equippedItems->Size; ++i)
                {
                    InventoryItem equippedItem = equippedItems->Items[i];
                    if (equippedItem.ItemId != 0 && equippedItem.ItemId == recommendedItem->ItemId)
                    {
                        isEquipped = true;
                        break;
                    }
                }

                if (!isEquipped)
                    return false;
            }

            return true;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
