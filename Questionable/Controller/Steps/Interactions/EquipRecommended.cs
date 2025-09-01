using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Questionable.Model;
using Questionable.Model.Questing;
using System;

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

    internal sealed unsafe class DoEquipRecommended(IClientState clientState, IChatGui chatGui, ICondition condition)
        : TaskExecutor<EquipTask>
    {
        private bool _checkedOrTriggeredEquipmentUpdate;
        private DateTime _continueAt = DateTime.MinValue;

        protected override bool Start()
        {
            if (condition[ConditionFlag.InCombat])
                return false;

            RecommendEquipModule.Instance()->SetupForClassJob((byte)clientState.LocalPlayer!.ClassJob.RowId);
            return true;
        }

        public override ETaskResult Update()
        {
            var recommendedEquipModule = RecommendEquipModule.Instance();
            if (recommendedEquipModule->IsUpdating)
                return ETaskResult.StillRunning;

            if (!_checkedOrTriggeredEquipmentUpdate)
            {
                if (!IsAllRecommendeGearEquipped())
                {
                    chatGui.Print("Equipping recommended gear.", CommandHandler.MessageTag, CommandHandler.TagColor);
                    recommendedEquipModule->EquipRecommendedGear();
                    _continueAt = DateTime.Now.AddSeconds(1);
                }

                _checkedOrTriggeredEquipmentUpdate = true;
                return ETaskResult.StillRunning;
            }

            return DateTime.Now >= _continueAt ? ETaskResult.TaskComplete : ETaskResult.StillRunning;
        }

        private bool IsAllRecommendeGearEquipped()
        {
            var recommendedEquipModule = RecommendEquipModule.Instance();
            InventoryManager* inventoryManager = InventoryManager.Instance();
            InventoryContainer* equippedItems =
                inventoryManager->GetInventoryContainer(InventoryType.EquippedItems);
            bool isAllEquipped = true;
            foreach (var recommendedItemPtr in recommendedEquipModule->RecommendedItems)
            {
                var recommendedItem = recommendedItemPtr.Value;
                if (recommendedItem == null || recommendedItem->ItemId == 0)
                    continue;

                bool isEquipped = false;
                for (int i = 0; i < equippedItems->Size; ++i)
                {
                    var equippedItem = equippedItems->Items[i];
                    if (equippedItem.ItemId != 0 && equippedItem.ItemId == recommendedItem->ItemId)
                    {
                        isEquipped = true;
                        break;
                    }
                }

                if (!isEquipped)
                    isAllEquipped = false;
            }

            return isAllEquipped;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
