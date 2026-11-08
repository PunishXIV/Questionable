using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.Interop;
using Questionable.Controller.Steps.Shared;
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

    internal sealed class BeforeDutyOrInstance(IObjectTable objectTable) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Duty &&
                step.InteractionType != EInteractionType.SinglePlayerDuty &&
                step.InteractionType != EInteractionType.Combat)
                yield break;

            if (sequence.Steps.Any(x => x.InteractionType is EInteractionType.EquipItem))
                yield break;
            if (objectTable[0] is IPlayerCharacter player &&
                ClassJobUtils.ClassToJobStone((Job)player.ClassJob.Value.RowId) is (Job targetJob, ushort jobStone))
            {
                bool hasJobStone = false;
                unsafe
                {
                    InventoryManager* inventoryManager = InventoryManager.Instance();
                    if (inventoryManager->GetItemCountInContainer(jobStone, InventoryType.ArmorySoulCrystal) != 0)
                        hasJobStone = true;
                }
                if (hasJobStone && quest.GetQuestInfo().ClassJobs.Contains(targetJob))
                {
                    yield return new EquipItem.Task(jobStone);
                    yield return new CreateGearset.Task();
                }
            }
            yield return new EquipTask();
        }
    }

    internal sealed class EquipTask : ITask
    {
        public override string ToString() => "EquipRecommended";
    }

    internal sealed unsafe class EquipExecutor(IChatGui chatGui, ICondition condition, Configuration config, StylistIpc stylist,
        ILogger<EquipExecutor> logger, TimeProvider? timeProvider = null)
        : TaskExecutor<EquipTask>
    {
        private bool _gearChecked;
        private DateTime _continueAt = DateTime.MinValue;
        private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
        private long? _stylistBusySince;
        // The source for this task only; a missing Stylist must not overwrite the saved preference.
        private EGearsetUpdateSource _source;

        protected override bool Start()
        {
            _stylistBusySince = null;
            _gearChecked = false;
            _continueAt = DateTime.MinValue;
            if (condition[ConditionFlag.InCombat])
                return false;

            _source = config.General.GearsetUpdateSource;
            if (!StylistIpc.IsInstalled && _source is EGearsetUpdateSource.Stylist)
            {
                if (!stylist.WarnedMissing)
                {
                    chatGui.Print("You've set Stylist to manage equipped gear, but it is not installed. Using Vanilla until it is.", CommandHandler.MessageTag, CommandHandler.TagColor);
                    stylist.WarnedMissing = true;
                }

                _source = EGearsetUpdateSource.Vanilla;
            }
            switch (_source)
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

        private ETaskResult FallBackToVanilla(string reason)
        {
            logger.LogWarning("Stylist {Reason}; equipping recommended gear with Vanilla instead", reason);
            _source = EGearsetUpdateSource.Vanilla;
            _gearChecked = false;
            RecommendEquipModule.Instance()->SetupForClassJob(PlayerState.Instance()->CurrentClassJobId);
            return ETaskResult.StillRunning;
        }

        public override ETaskResult Update()
        {
            switch (_source)
            {
                case EGearsetUpdateSource.Vanilla:
                    RecommendEquipModule* recommendedEquipModule = RecommendEquipModule.Instance();
                    if (recommendedEquipModule->IsUpdating)
                        return ETaskResult.StillRunning;

                    if (!_gearChecked)
                    {
                        if (!IsRecommendedGearEquipped())
                        {
                            chatGui.Print("Equipping recommended gear.", CommandHandler.MessageTag, CommandHandler.TagColor);
                            recommendedEquipModule->EquipRecommendedGear();
                            _continueAt = DateTime.Now.AddSeconds(0.25);
                        }

                        _gearChecked = true;
                        return ETaskResult.StillRunning;
                    }

                    break;
                case EGearsetUpdateSource.Stylist:
                    return UpdateStylist(stylist.IsBusy, stylist.UpdateGearset);
            }

            return DateTime.Now >= _continueAt ? ETaskResult.TaskComplete : ETaskResult.StillRunning;
        }

        internal ETaskResult UpdateStylist(bool? isBusy, Func<bool> updateGearset)
        {
            if (isBusy is not { } busy)
                return FallBackToVanilla("became unavailable");
            if (busy)
            {
                _stylistBusySince ??= _clock.GetTimestamp();
                // Stylist may still be changing equipment; do not start a competing Vanilla update.
                if (_clock.GetElapsedTime(_stylistBusySince.Value) >= TimeSpan.FromSeconds(30))
                    throw new TaskException("Stylist stayed busy for 30 seconds. Finish or stop its work, then restart Questionable.");
                return ETaskResult.StillRunning;
            }

            _stylistBusySince = null;
            if (!_gearChecked)
            {
                if (!updateGearset())
                    return FallBackToVanilla("rejected the gear update");
                _gearChecked = true;
                _continueAt = DateTime.Now.AddSeconds(0.25);
                return ETaskResult.StillRunning;
            }

            return DateTime.Now >= _continueAt ? ETaskResult.TaskComplete : ETaskResult.StillRunning;
        }

        private bool IsRecommendedGearEquipped()
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
