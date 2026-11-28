using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.Interop;
using Questionable.Controller.Steps.Shared;
namespace Questionable.Controller.Steps.Interactions;

internal static class EquipRecommended
{
    internal static unsafe bool GearsetMatches(ref RaptureGearsetModule.GearsetEntry saved,
        ReadOnlySpan<InventoryItem> equipped, uint classJob)
    {
        if (saved.ClassJob != classJob || equipped.Length != saved.Items.Length)
            return false;

        for (int i = 0; i < saved.Items.Length; ++i)
        {
            ref readonly InventoryItem item = ref equipped[i];
            ref RaptureGearsetModule.GearsetItem recorded = ref saved.Items[i];
            // Equipped slots normally hold real items. Let the game handle unresolved linked items.
            if (item.IsSymbolic)
                return false;
            uint itemId = item.ItemId;
            if (itemId != 0 && (item.Flags & InventoryItem.ItemFlags.HighQuality) != 0)
                itemId += 1_000_000;
            if (recorded.ItemId != itemId)
                return false;
            if (itemId == 0)
                continue;
            if (recorded.GlamourId != item.GlamourId ||
                recorded.Stain0Id != item.Stains[0] || recorded.Stain1Id != item.Stains[1] ||
                !recorded.Materia.SequenceEqual(item.Materia) ||
                !recorded.MateriaGrades.SequenceEqual(item.MateriaGrades))
                return false;
        }

        return true;
    }

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
                    SaveCurrentGearset();
                    break;
            }

            return true;
        }

        private static void SaveCurrentGearset()
        {
            RaptureGearsetModule* gearsets = RaptureGearsetModule.Instance();
            InventoryManager* inventory = InventoryManager.Instance();
            PlayerState* player = PlayerState.Instance();
            if (gearsets == null || inventory == null || player == null)
                throw new TaskException("Current equipment is unavailable.");

            int index = gearsets->CurrentGearsetIndex;
            if (index < 0 || index >= gearsets->Entries.Length || !gearsets->IsValidGearset(index))
                return;

            InventoryContainer* equipped = inventory->GetInventoryContainer(InventoryType.EquippedItems);
            if (equipped == null || equipped->Items == null)
                throw new TaskException("Current equipment is unavailable.");

            if (!GearsetMatches(ref gearsets->Entries[index],
                    new ReadOnlySpan<InventoryItem>(equipped->Items, equipped->Size), player->CurrentClassJobId))
                gearsets->UpdateGearset(index);
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
                if (_clock.GetElapsedTime(_stylistBusySince.Value) < TimeSpan.FromSeconds(30))
                    return ETaskResult.StillRunning;

                logger.LogWarning("Stylist stayed busy for 30 seconds; skipping the gear update");
                chatGui.Print("Stylist stayed busy for 30 seconds, so the gear update was skipped this time.",
                    CommandHandler.MessageTag, CommandHandler.TagColor);
                return ETaskResult.TaskComplete;
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
