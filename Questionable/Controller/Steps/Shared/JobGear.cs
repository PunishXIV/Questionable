using ECommons.ExcelServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Movement;
using static Questionable.Controller.Steps.ITaskExecutor;
using Quest = Questionable.Domain.Quest;
namespace Questionable.Controller.Steps.Shared;

internal static class JobGear
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);
    private static readonly Dictionary<string, DateTime> RetryAt = [];

    // Quest dialogue and yes/no handling stay out of the vendor's windows while this is set.
    internal static bool Active { get; private set; }

    internal sealed record Option(int Level, int ItemLevel, bool Unlocked, bool Collected);

    internal static Option? Choose(IReadOnlyList<Option> options, int playerLevel, int equippedItemLevel)
    {
        Option? best = options.Where(x => x.Unlocked && x.Level <= playerLevel).MaxBy(x => x.Level);
        return best is { Collected: false } && best.ItemLevel > equippedItemLevel ? best : null;
    }

    internal static string Entry(Job job, int level) => string.Create(CultureInfo.InvariantCulture, $"{job}:{level}");

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
        ICondition condition,
        IDalamudPluginInterface pluginInterface,
        IChatGui chatGui,
        QuestPriorityManager priorityManager,
        ILogger<Factory> logger) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (!configuration.General.CollectJobGear ||
                !BetweenQuests.CanDetour(quest, step, questFunctions, condition, gameFunctions, priorityManager) ||
                ProfileController.CurrentContentId is not { } contentId)
                return [];

            return CreateTasks(contentId, level: null, force: false, out _);
        }

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
                reason = string.Create(CultureInfo.InvariantCulture, $"No unlocked job gear set that {job} at level {playerLevel} can wear{(level != null ? string.Create(CultureInfo.InvariantCulture, $" at level {level}") : "")}.");
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
                reason = string.Create(CultureInfo.InvariantCulture, $"You already have every piece of the level {choice.Level} set for {job}.");
                return [];
            }

            if (inventory->GetEmptySlotsInBag() < missing.Count)
            {
                RetryAt[key] = DateTime.Now + RetryAfter;
                reason = _LF("Your level {0} job gear is ready to collect, but you need {1} free inventory slots. Trying again in 30 minutes.",
                    choice.Level, missing.Count);
                chatGui.Print(reason, CommandHandler.MessageTag, CommandHandler.TagColor);
                return [];
            }

            logger.LogInformation("Collecting level {Level} job gear from {Shop}: {Items}", choice.Level,
                chosen.ShopName, string.Join(", ", missing));
            reason = string.Create(CultureInfo.InvariantCulture, $"Collecting the level {choice.Level} set for {job} from {chosen.ShopName} ({missing.Count} pieces).");
            return
            [
                new Travel.Task(chosen.Vendor.TerritoryId, chosen.Vendor.Position, chosen.Vendor.NpcId),
                new Take(chosen.Vendor.NpcId, chosen.ShopName, chosen.Items, contentId, choice.Level),
                new EquipRecommended.EquipTask(),
                new Done(key, RetryStep: !force),
            ];
        }

    }

    internal sealed record Take(uint NpcId, string ShopName, IReadOnlyList<uint> Items, string ContentId, int Level)
        : ITask
    {
        public override string ToString() => $"JobGear({Level})";
    }

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
        private bool _collected;

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
                if (!_collected)
                {
                    _collected = true;
                    _giveUpAt = DateTime.Now + GiveUpAfter;
                    Remember(configuration, Task.ContentId, (Job)PlayerState.Instance()->CurrentClassJobId, Task.Level);
                    pluginInterface.SavePluginConfig(configuration);
                    logger.LogInformation("Collected level {Level} job gear", Task.Level);
                }

                if (CloseShop() && DateTime.Now < _giveUpAt)
                    return ETaskResult.StillRunning;

                Active = false;
                return ETaskResult.TaskComplete;
            }

            if (DateTime.Now > _giveUpAt)
            {
                Finish();
                throw new TaskException(string.Create(CultureInfo.InvariantCulture, $"the vendor's shop didn't hand out the gear within {GiveUpAfter.TotalSeconds} seconds"));
            }

            if (DateTime.Now < _nextTry)
                return ETaskResult.StillRunning;
            _nextTry = DateTime.Now.AddSeconds(1);

            if (GenericHelpers.TryGetAddonMaster<AddonMaster.SelectYesno>(out var yesno) && yesno.IsAddonReady)
            {
                yesno.Yes();
                return ETaskResult.StillRunning;
            }

            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("FreeShop", out var freeShop) &&
                GenericHelpers.IsAddonReady(freeShop))
                return TakeFromFreeShop(missing);

            // NPCs with several options open one menu or the other.
            if (GenericHelpers.TryGetAddonMaster<AddonMaster.SelectIconString>(out var iconMenu) && iconMenu.IsAddonReady)
                return PickShop(iconMenu.Base, [.. iconMenu.Entries.Select(x => x.Text)]);
            if (GenericHelpers.TryGetAddonMaster<AddonMaster.SelectString>(out var menu) && menu.IsAddonReady)
                return PickShop(menu.Base, [.. menu.Entries.Select(x => x.Text)]);

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

        private unsafe ETaskResult TakeFromFreeShop(List<uint> missing)
        {
            AgentFreeShop* agent = AgentFreeShop.Instance();
            if (agent == null || !agent->AgentInterface.IsAgentActive() || agent->IsInteractionBlocked ||
                agent->IsLoadingItems)
                return ETaskResult.StillRunning;

            for (int i = 0; i < agent->ItemCount; i++)
            {
                var item = agent->Items[i];
                if (!missing.Contains(item.ItemId))
                    continue;
                if (item.IsOwned || item.IsUnavailable)
                    throw new TaskException($"the shop won't hand out item {item.ItemId} (owned: {item.IsOwned})");

                logger.LogInformation("Taking {ItemId} from {Shop}", item.ItemId, Task.ShopName);
                AtkValue result = new();
                AtkValue* values = stackalloc AtkValue[2];
                values[0].Type = AtkValueType.Int;
                values[0].Int = 0;
                values[1].Type = AtkValueType.Int;
                values[1].Int = i;
                agent->AgentInterface.ReceiveEvent(&result, values, 2, 0);
                _nextTry = DateTime.Now.AddSeconds(1);
                return ETaskResult.StillRunning;
            }

            throw new TaskException($"item {missing[0]} isn't in '{Task.ShopName}'");
        }

        private unsafe ETaskResult PickShop(AtkUnitBase* addon, List<string> entries)
        {
            int index = entries.FindIndex(x => GameFunctions.GameStringEquals(x, Task.ShopName));
            if (index < 0)
                throw new TaskException($"the vendor's menu has no '{Task.ShopName}' ({string.Join(", ", entries)})");

            logger.LogInformation("Picking {Shop} in the vendor's menu", Task.ShopName);
            addon->FireCallbackInt(index);
            _nextTry = DateTime.Now.AddSeconds(2);
            return ETaskResult.StillRunning;
        }

        public void StopNow() => Finish();

        // Close(true) leaves the shop open; the callback closes it.
        private unsafe bool CloseShop()
        {
            if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>("FreeShop", out var freeShop) || !freeShop->IsVisible)
                return false;

            if (DateTime.Now >= _nextTry)
            {
                _nextTry = DateTime.Now.AddSeconds(1);
                freeShop->FireCallbackInt(-1);
            }

            return true;
        }

        private static unsafe void Finish()
        {
            Active = false;
            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("FreeShop", out var freeShop) && freeShop->IsVisible)
                freeShop->FireCallbackInt(-1);
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

    internal sealed class DoneExecutor : TaskExecutor<Done>
    {
        protected override bool Start() => true;
        public override ETaskResult Update() => Task.RetryStep ? ETaskResult.RetryStep : ETaskResult.TaskComplete;
        public override bool ShouldInterruptOnDamage() => false;
    }
}
