using ECommons.ExcelServices;
using System.Diagnostics;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Questionable.Controller.Steps.Common;
using Questionable.Model.Questing;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller.Steps.Shared;

internal static class Craft
{
    internal sealed class Factory : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Craft)
                return [];

            return
            [
                new MountStep.UnmountTask(),
                new CraftTask(quest, sequence.Sequence, step)
            ];
        }
    }

    internal sealed record CraftTask
    (
        Quest Quest,
        byte Sequence,
        QuestStep Step) : ITask
    {
        public uint? ItemId => Step.ItemId;
        public int? ItemCount => Step.ItemCount;
        public override string ToString() => $"Craft({ItemCount}x {ItemId})";
    }

    internal static bool HasItemTarget(CraftTask task)
    {
        if (task.ItemId.HasValue || task.ItemCount.HasValue)
        {
            if (task.ItemId is null or 0 || task.ItemCount is null or <= 0)
                throw new TaskException("Craft requires both an item ID and a positive item count");
            return true;
        }

        // Legacy class-quest paths rely on built-in lists without declaring their outputs.
        if (task.Quest.Id is not QuestId)
            throw new TaskException("Artisan list-only crafting requires a normal quest ID");
        return false;
    }

    internal static bool IsQuestObjectiveMet(CraftTask task, bool questComplete, QuestProgressInfo? questWork) =>
        QuestWorkUtils.HasCompletionFlags(task.Step.CompletionQuestVariablesFlags) &&
        (questComplete || questWork != null &&
            (questWork.Sequence > task.Sequence ||
             questWork.Sequence == task.Sequence && QuestWorkUtils.MatchesQuestWork(task.Step.CompletionQuestVariablesFlags, questWork)));

    internal static IReadOnlyList<CraftingItem> GetItemTargets(CraftTask task) => HasItemTarget(task)
        ? [QuestCraftingRequirements.ResolveItem(task.Quest.Id,
            new CraftingItem(task.ItemId!.Value, task.ItemCount!.Value, task.Step.ItemQuality))]
        : QuestCraftingRequirements.GetLegacyItems(task.Quest.Id, task.Sequence);

    internal static bool AreItemsSatisfied(IReadOnlyList<CraftingItem> targets, Func<CraftingItem, int> countItems) =>
        targets.Count > 0 && targets.All(item => countItems(item) >= item.ItemCount);

    internal static bool TryStartQuestList(ArtisanIpc artisanIpc, ElementId questId,
        uint? itemId = null, IReadOnlySet<uint>? requiredItemIds = null)
    {
        if (questId is not QuestId)
            throw new TaskException("Artisan quest lists require a normal quest ID");

        // A full list may consume an explicitly requested intermediate (e.g. Natron in CUL 268).
        if (itemId.HasValue && (requiredItemIds is not { Count: > 0 } || !requiredItemIds.Contains(itemId.Value)))
            return false;

        ArtisanStartResult result = artisanIpc.CraftList(questId.Value.ToInt() + 65536);
        return result switch
        {
            ArtisanStartResult.Submitted => true,
            ArtisanStartResult.MissingList => false,
            _ => throw new TaskException($"Artisan list request was not accepted ({result}); recipe fallback is unsafe"),
        };
    }

    internal static int GetCraftCount(int remainingItems, int yield)
    {
        if (remainingItems <= 0 || yield <= 0)
            throw new TaskException("Craft quantity and recipe yield must be positive");
        return 1 + (remainingItems - 1) / yield;
    }

    internal static uint SelectRecipe(Job job, IReadOnlyList<uint> recipes)
    {
        int index = (int)job - (int)Job.CRP;
        return index >= 0 && index < recipes.Count && recipes[index] != 0
            ? recipes[index]
            : recipes.FirstOrDefault(x => x != 0);
    }

    internal static int CountMatchingItems(EItemQuality quality, int normal, int high, int collectable) => quality switch
    {
        EItemQuality.NQ => normal,
        EItemQuality.HQ => high,
        EItemQuality.Any => normal + high + collectable,
        _ => throw new TaskException($"Unsupported craft item quality {quality}"),
    };

    internal sealed class Progress(ArtisanStatus initialStatus, bool requireObjective = true)
    {
        internal static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(15);
        internal static readonly TimeSpan IdleGrace = TimeSpan.FromSeconds(2);
        private bool _observedBusy = initialStatus == ArtisanStatus.Busy;
        private TimeSpan? _idleSince;

        public bool Update(ArtisanStatus status, bool objectiveMet, TimeSpan elapsed)
        {
            if (status == ArtisanStatus.Paused)
                throw new TaskException("Artisan is paused or has a stop request; cancel its work manually before restarting Questionable");

            if (status == ArtisanStatus.Busy)
            {
                _observedBusy = true;
                _idleSince = null;
                return false;
            }

            if (status == ArtisanStatus.Queued)
            {
                _idleSince = null;
                if (!_observedBusy && elapsed >= StartTimeout)
                    throw new TaskException("Artisan work remained queued without starting for 15 seconds; cancel queued work manually before restarting");
                return false;
            }

            _idleSince ??= elapsed;
            if (elapsed - _idleSince.Value < IdleGrace)
                return false;
            if (requireObjective ? objectiveMet : _observedBusy)
                return true;
            if (_observedBusy)
                throw new TaskException("Artisan stopped without meeting the craft count/quality or quest objective");
            if (elapsed >= StartTimeout)
                throw new TaskException("Artisan did not start within 15 seconds and the craft objective is unmet; check for queued work before restarting");
            return false;
        }
    }

    internal sealed class DoCraft
    (
        IDataManager dataManager,
        ArtisanIpc artisanIpc,
        ILogger<DoCraft> logger,
        QuestFunctions questFunctions,
        CraftingOperationState craftingOperation) : TaskExecutor<CraftTask>, ITaskExecutor.IStoppableTaskExecutor
    {
        private bool _requireObjective;
        private IReadOnlyList<CraftingItem> _itemTargets = [];
        private int[] _previousCounts = [];
        private Progress _progress = null!;
        private readonly Stopwatch _elapsed = new();
        private Guid? _operationId;

        protected override unsafe bool Start()
        {
            bool hasItemTarget = HasItemTarget(Task);
            _itemTargets = GetItemTargets(Task);
            _requireObjective = _itemTargets.Count > 0 || QuestWorkUtils.HasCompletionFlags(Task.Step.CompletionQuestVariablesFlags);
            _previousCounts = _itemTargets.Select(GetOwnedItemCount).ToArray();
            IReadOnlySet<uint>? requiredItemIds = null;
            if (hasItemTarget && Task.Quest.Id is QuestId questId &&
                dataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>().GetRowOrDefault(questId.Value + 65536u) is { } questRow)
            {
                requiredItemIds = QuestCraftingRequirements.GetRequiredItemIds(questRow.QuestParams
                    .Select(x => (x.ScriptInstruction.ExtractText(), x.ScriptArg)));
            }

            IReadOnlyList<CraftingItem>? listTargets = CraftingBatch.Resolve(Task, requiredItemIds);
            if ((listTargets != null && AreItemsSatisfied(listTargets, GetOwnedItemCount)) || IsObjectiveMet())
            {
                logger.LogInformation("Craft objective is already met for {QuestId}", Task.Quest.Id);
                return false;
            }

            if (!hasItemTarget && questFunctions.GetQuestProgressInfo(Task.Quest.Id) == null)
                throw new TaskException("Quest state is unavailable; cannot verify this Artisan list's objective");

            if (listTargets != null)
            {
                int[] listCounts = listTargets.Select(GetOwnedItemCount).ToArray();
                bool listRequiresObjective = listTargets.Count > 0 || QuestWorkUtils.HasCompletionFlags(Task.Step.CompletionQuestVariablesFlags);
                if (TryStartQuestList(artisanIpc, Task.Quest.Id, Task.ItemId, requiredItemIds))
                {
                    _itemTargets = listTargets;
                    _previousCounts = listCounts;
                    _requireObjective = listRequiresObjective;
                    return TrackSubmission();
                }
            }

            if (!hasItemTarget)
                throw new TaskException("Artisan has no list for this quest and no item target is available for recipe fallback");

            RecipeLookup recipeLookup = dataManager.GetExcelSheet<RecipeLookup>().GetRowOrDefault(Task.ItemId!.Value) ??
                                         throw new TaskException($"Item {Task.ItemId} is not craftable");
            QuestProgressInfo? questWork = questFunctions.GetQuestProgressInfo(Task.Quest.Id);
            PlayerState* playerState = PlayerState.Instance();
            if (playerState == null)
                throw new TaskException("Player state is unavailable; cannot select a crafting job");
            Job job = questWork != null && questWork.ClassJob.IsCrafter()
                ? questWork.ClassJob
                : (Job)playerState->CurrentClassJobId;
            uint recipeId = SelectRecipe(job,
            [
                recipeLookup.CRP.RowId,
                recipeLookup.BSM.RowId,
                recipeLookup.ARM.RowId,
                recipeLookup.GSM.RowId,
                recipeLookup.LTW.RowId,
                recipeLookup.WVR.RowId,
                recipeLookup.ALC.RowId,
                recipeLookup.CUL.RowId,
            ]);

            if (recipeId == 0)
                throw new TaskException($"Unable to determine recipe for item {Task.ItemId}");

            Recipe recipe = dataManager.GetExcelSheet<Recipe>().GetRowOrDefault(recipeId) ??
                            throw new TaskException($"Recipe {recipeId} does not exist");
            CraftingItem target = _itemTargets.Single();
            int startingItemCount = _previousCounts[0];
            int remainingItemCount = target.ItemCount - startingItemCount;
            int crafts = GetCraftCount(remainingItemCount, recipe.AmountResult);
            logger.LogInformation(
                "Starting craft for item {ItemId} with recipe {RecipeId} for {RemainingItemCount} items (quality: {Quality}, owned: {OwnedCount})",
                Task.ItemId, recipeId, remainingItemCount, target.Quality, startingItemCount);
            ArtisanStartResult result = artisanIpc.CraftItem(checked((ushort)recipeId), crafts);
            if (result != ArtisanStartResult.Submitted)
                throw new TaskException($"Failed to submit Artisan recipe {recipeId} ({result}); check Artisan before restarting");

            return TrackSubmission();
        }

        private bool TrackSubmission()
        {
            // IsBusy includes queued startup/cleanup. SetStopRequest only pauses lists; Artisan has no cancel for just our work.
            _elapsed.Restart();
            _operationId = craftingOperation.OperationId ??
                throw new TaskException("Artisan submission was not tracked; check Artisan before restarting");
            ArtisanStatus status = artisanIpc.GetStatus();
            craftingOperation.ObserveStatus(status);
            _progress = new Progress(status, _requireObjective);
            if (_requireObjective)
                logger.LogInformation("Artisan request submitted; waiting for activity and verified craft output");
            else
                logger.LogWarning("Legacy list-only craft for {QuestId} has no item target or completion flags; waiting for active Artisan execution then stable idle, but outputs cannot be verified", Task.Quest.Id);
            return true;
        }

        public override unsafe ETaskResult Update()
        {
            ArtisanStatus status = artisanIpc.GetStatus();
            craftingOperation.ObserveStatus(status);
            if (!craftingOperation.CanContinue(_operationId))
                throw new TaskException(craftingOperation.BlockReason ?? "Artisan is no longer crafting for Questionable");

            for (int i = 0; i < _itemTargets.Count; ++i)
            {
                CraftingItem item = _itemTargets[i];
                int currentCount = GetOwnedItemCount(item);
                if (currentCount != _previousCounts[i])
                {
                    logger.LogInformation("Craft progress for item {ItemId}: {Current}/{Target} (quality: {Quality})",
                        item.ItemId, currentCount, item.ItemCount, item.Quality);
                    _previousCounts[i] = currentCount;
                }
            }

            if (_progress.Update(status, IsObjectiveMet(), _elapsed.Elapsed))
            {
                if (_requireObjective)
                    logger.LogInformation("Craft objective met for {QuestId}, closing crafting window", Task.Quest.Id);
                else
                    logger.LogInformation("Legacy Artisan list finished for {QuestId}; outputs remain unverified, closing crafting window", Task.Quest.Id);
                if (CloseCraftingLog() && craftingOperation.TryComplete(_operationId))
                    return ETaskResult.TaskComplete;
            }

            return ETaskResult.StillRunning;
        }

        public void StopNow() => craftingOperation.Abandon(_operationId);

        private static unsafe bool CloseCraftingLog()
        {
            AgentRecipeNote* agentRecipeNote = AgentRecipeNote.Instance();
            if (agentRecipeNote != null && agentRecipeNote->IsAgentActive())
            {
                uint addonId = agentRecipeNote->GetAddonId();
                if (addonId == 0)
                    return false;

                AtkUnitBase* addon = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonById((ushort)addonId);
                if (addon != null)
                {
                    addon->FireCallbackInt(-1);
                    return true;
                }
            }

            return true;
        }

        private bool IsObjectiveMet()
        {
            if (_itemTargets.Count > 0)
                return AreItemsSatisfied(_itemTargets, GetOwnedItemCount);
            if (!_requireObjective)
                return false;
            QuestProgressInfo? questWork = questFunctions.GetQuestProgressInfo(Task.Quest.Id);
            return IsQuestObjectiveMet(Task, questFunctions.IsQuestComplete(Task.Quest.Id), questWork);
        }

        private static unsafe int GetOwnedItemCount(CraftingItem item)
        {
            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                throw new TaskException("Inventory is unavailable; cannot verify the craft objective");
            return CountMatchingItems(item.Quality,
                inventoryManager->GetInventoryItemCount(item.ItemId, isHq: false, checkEquipped: false),
                inventoryManager->GetInventoryItemCount(item.ItemId, isHq: true, checkEquipped: false),
                inventoryManager->GetInventoryItemCount(item.ItemId + 500000, isHq: false, checkEquipped: false));
        }

        // On a crafting class combat doesn't make sense, and we can't change classes in combat.
        public override bool ShouldInterruptOnDamage() => false;
    }
}
