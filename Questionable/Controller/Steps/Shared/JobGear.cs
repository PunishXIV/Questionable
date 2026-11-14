using ECommons.ExcelServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Movement;
using static Questionable.Controller.Steps.ITaskExecutor;
using Quest = Questionable.Domain.Quest;
namespace Questionable.Controller.Steps.Shared;

// Collects the free job gear for the current job between quests, then puts on the best gear.
internal static class JobGear
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);
    private static readonly Dictionary<string, DateTime> RetryAt = [];

    // Quest dialogue and yes/no handling stay out of the vendor's windows while this is set.
    internal static bool Active { get; private set; }

    internal sealed record Option(int Level, int ItemLevel, bool Unlocked, bool Collected);

    // Only the highest unlocked set the job can wear counts; a lower set is never the fallback.
    internal static Option? Choose(IReadOnlyList<Option> options, int playerLevel, int equippedItemLevel)
    {
        Option? best = options.Where(x => x.Unlocked && x.Level <= playerLevel).MaxBy(x => x.Level);
        return best is { Collected: false } && best.ItemLevel > equippedItemLevel ? best : null;
    }

    internal static string Entry(Job job, int level) => $"{job}:{level}";

    internal static void Remember(Configuration configuration, string contentId, Job job, int level)
    {
        if (!configuration.JobGear.Collected.TryGetValue(contentId, out List<string>? sets))
            configuration.JobGear.Collected[contentId] = sets = [];
        string entry = Entry(job, level);
        if (!sets.Contains(entry))
            sets.Add(entry);
    }

    private static unsafe List<uint> Missing(IReadOnlyList<uint> items)
    {
        InventoryManager* inventory = InventoryManager.Instance();
        return inventory == null ? [.. items] : items.Where(x => inventory->GetInventoryItemCount(x) == 0).ToList();
    }

    internal sealed class Factory(
        Configuration configuration,
        QuestFunctions questFunctions,
        JobGearData jobGearData,
        GearStatsCalculator gearStats,
        GameFunctions gameFunctions,
        IClientState clientState,
        ICondition condition,
        IDalamudPluginInterface pluginInterface,
        IChatGui chatGui,
        ILogger<Factory> logger) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            // Between quests only: nothing is in progress, and the accept step's position leads back afterwards.
            if (!configuration.General.CollectJobGear ||
                step is not { InteractionType: EInteractionType.AcceptQuest, Position: not null } ||
                questFunctions.IsQuestAccepted(quest.Id) || IsBusy() ||
                ProfileController.CurrentContentId is not { } contentId)
                return [];

            return CreateTasks(contentId, level: null, force: false, out _);
        }

        // Force is for /qst jobgear: no between-quests rule, collected record, retry pause or upgrade check.
        internal unsafe List<ITask> CreateTasks(string contentId, int? level, bool force, out string reason)
        {
            reason = "";
            PlayerState* playerState = PlayerState.Instance();
            InventoryManager* inventory = InventoryManager.Instance();
            if (playerState == null || inventory == null)
                return [];
            InventoryContainer* equipped = inventory->GetInventoryContainer(InventoryType.EquippedItems);
            if (equipped == null)
                return [];

            Job job = (Job)playerState->CurrentClassJobId;
            int playerLevel = playerState->CurrentLevel;
            List<string> collected = configuration.JobGear.Collected.GetValueOrDefault(contentId) ?? [];
            List<(Option Option, JobGearData.GearSet Set)> options = [];
            foreach (JobGearData.Vendor vendor in JobGearData.Vendors)
            {
                if (jobGearData.GetSet(vendor, job) is { } set)
                    options.Add((new Option(vendor.Level, set.ItemLevel, questFunctions.IsQuestComplete(set.UnlockQuest),
                        collected.Contains(Entry(job, vendor.Level))), set));
            }

            Option? choice = force
                ? options.Select(x => x.Option)
                    .Where(x => x.Unlocked && x.Level <= playerLevel && (level == null || x.Level == level))
                    .MaxBy(x => x.Level)
                : Choose(options.Select(x => x.Option).ToList(), playerLevel,
                    gearStats.CalculateAverageItemLevel(equipped));
            if (choice == null)
            {
                reason = $"No unlocked job gear set that {job} at level {playerLevel} can wear{(level != null ? $" at level {level}" : "")}.";
                return [];
            }

            JobGearData.GearSet chosen = options.First(x => x.Option == choice).Set;
            string key = $"{contentId}:{Entry(job, choice.Level)}";
            if (!force && RetryAt.TryGetValue(key, out DateTime retryAt) && DateTime.Now < retryAt)
                return [];

            List<uint> missing = Missing(chosen.Items);
            if (missing.Count == 0)
            {
                Remember(configuration, contentId, job, choice.Level);
                pluginInterface.SavePluginConfig(configuration);
                reason = $"You already have every piece of the level {choice.Level} set for {job}.";
                return [];
            }

            if (inventory->GetEmptySlotsInBag() < missing.Count)
            {
                RetryAt[key] = DateTime.Now + RetryAfter;
                reason = _LF("Your level {0} job gear is ready to collect, but your bags need {1} free slots. Trying again in 30 minutes.",
                    choice.Level, missing.Count);
                chatGui.Print(reason, CommandHandler.MessageTag, CommandHandler.TagColor);
                return [];
            }

            logger.LogInformation("Collecting level {Level} job gear from {Shop}: {Items}", choice.Level,
                chosen.ShopName, string.Join(", ", missing));
            reason = $"Collecting the level {choice.Level} set for {job} from {chosen.ShopName} ({missing.Count} pieces).";
            return
            [
                .. Travel(chosen.Vendor),
                new Take(chosen.Vendor.NpcId, chosen.ShopName, chosen.Items, contentId, choice.Level),
                new EquipRecommended.EquipTask(),
                new Done(key, RetryStep: !force),
            ];
        }

        private IEnumerable<ITask> Travel(JobGearData.Vendor vendor)
        {
            QuestStep step = new(EInteractionType.Interact, vendor.NpcId, vendor.Position, vendor.TerritoryId)
            {
                StopDistance = 3
            };
            if (clientState.TerritoryType != vendor.TerritoryId)
                yield return new AetheryteShortcut.Task(step, null, EAetheryteLocation.None, vendor.TerritoryId);
            yield return new WaitNavmesh.Task();
            yield return new MoveTask(step, vendor.Position);
        }

        private bool IsBusy() =>
            condition[ConditionFlag.InCombat] || condition[ConditionFlag.BoundByDuty] ||
            condition[ConditionFlag.BoundByDuty56] || condition[ConditionFlag.BoundByDuty95] ||
            condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51] ||
            condition[ConditionFlag.OccupiedInCutSceneEvent] || condition[ConditionFlag.WatchingCutscene] ||
            condition[ConditionFlag.Crafting] || condition[ConditionFlag.Gathering] ||
            condition[ConditionFlag.Fishing] || gameFunctions.IsOccupied();
    }

    internal sealed record Take(uint NpcId, string ShopName, IReadOnlyList<uint> Items, string ContentId, int Level)
        : ITask
    {
        public override string ToString() => $"JobGear({Level})";
    }

    // Talks to the vendor, picks the role's shop in the menu and takes each missing piece.
    internal sealed class TakeExecutor(
        GameFunctions gameFunctions,
        ICondition condition,
        Configuration configuration,
        IDalamudPluginInterface pluginInterface,
        ILogger<TakeExecutor> logger) : TaskExecutor<Take>, IStoppable
    {
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(60);
        private DateTime _giveUpAt;
        private DateTime _nextTry;

        protected override bool Start()
        {
            Active = true;
            _giveUpAt = DateTime.Now + GiveUpAfter;
            _nextTry = DateTime.MinValue;
            return true;
        }

        public override unsafe ETaskResult Update()
        {
            List<uint> missing = Missing(Task.Items);
            if (missing.Count == 0)
            {
                Finish();
                Remember(configuration, Task.ContentId, (Job)PlayerState.Instance()->CurrentClassJobId, Task.Level);
                pluginInterface.SavePluginConfig(configuration);
                logger.LogInformation("Collected level {Level} job gear", Task.Level);
                return ETaskResult.TaskComplete;
            }

            if (DateTime.Now > _giveUpAt)
            {
                Finish();
                throw new TaskException($"the vendor's shop didn't hand out the gear within {GiveUpAfter.TotalSeconds} seconds");
            }

            if (DateTime.Now < _nextTry)
                return ETaskResult.StillRunning;
            _nextTry = DateTime.Now.AddSeconds(1);

            if (GenericHelpers.TryGetAddonMaster<AddonMaster.SelectYesno>(out var yesno) && yesno.IsAddonReady)
            {
                yesno.Yes();
                return ETaskResult.StillRunning;
            }

            if (GenericHelpers.TryGetAddonMaster<AddonMaster.ShopExchangeCurrencyDialog>(out var dialog) &&
                dialog.IsAddonReady)
            {
                dialog.Exchange();
                return ETaskResult.StillRunning;
            }

            if (GenericHelpers.TryGetAddonMaster<AddonMaster.ShopExchangeCurrency>(out var shop) && shop.IsAddonReady)
            {
                var item = shop.BasicShopItems.FirstOrDefault(x => missing.Contains(x.ItemId)) ??
                           throw new TaskException($"item {missing[0]} isn't in '{Task.ShopName}'");
                logger.LogInformation("Taking {ItemId} from {Shop}", item.ItemId, Task.ShopName);
                item.Select(1);
                return ETaskResult.StillRunning;
            }

            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("ShopExchangeItem", out var itemShop) &&
                GenericHelpers.IsAddonReady(itemShop))
                throw new TaskException("the vendor opened a shop window Questionable can't use yet (ShopExchangeItem)");

            if (GenericHelpers.TryGetAddonMaster<AddonMaster.SelectString>(out var menu) && menu.IsAddonReady)
            {
                foreach (AddonMaster.SelectString.Entry entry in menu.Entries)
                {
                    if (entry.Text.Trim() == Task.ShopName.Trim())
                    {
                        entry.Select();
                        return ETaskResult.StillRunning;
                    }
                }

                throw new TaskException($"the vendor's menu has no '{Task.ShopName}' ({string.Join(", ", menu.Entries.Select(x => x.Text))})");
            }

            // TextAdvance moves the vendor's talk along.
            if (condition[ConditionFlag.OccupiedInEvent] || condition[ConditionFlag.OccupiedInQuestEvent])
                return ETaskResult.StillRunning;

            if (condition[ConditionFlag.Mounted])
            {
                gameFunctions.Unmount();
                return ETaskResult.StillRunning;
            }

            if (gameFunctions.FindObjectByDataId(Task.NpcId) is { } vendor)
            {
                gameFunctions.InteractWith(vendor);
                _nextTry = DateTime.Now.AddSeconds(2);
            }

            return ETaskResult.StillRunning;
        }

        public void StopNow() => Finish();

        private static unsafe void Finish()
        {
            Active = false;
            if (GenericHelpers.TryGetAddonMaster<AddonMaster.ShopExchangeCurrency>(out var shop) && shop.IsAddonReady)
                shop.Base->Close(true);
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record Done(string RetryKey, bool RetryStep) : IOptionalGroupEnd
    {
        public void OnFailed(Exception exception)
        {
            Active = false;
            RetryAt[RetryKey] = DateTime.Now + RetryAfter;
            Svc.Chat.Print(_LF("Couldn't collect your job gear: {0}. Carrying on; trying again in 30 minutes.",
                exception.Message.TrimEnd('.')), CommandHandler.MessageTag, CommandHandler.TagColor);
        }

        public override string ToString() => "JobGearDone";
    }

    // Plans the accept step again, which walks back to the quest giver.
    internal sealed class DoneExecutor : TaskExecutor<Done>
    {
        protected override bool Start() => true;
        public override ETaskResult Update() => Task.RetryStep ? ETaskResult.RetryStep : ETaskResult.TaskComplete;
        public override bool ShouldInterruptOnDamage() => false;
    }
}
