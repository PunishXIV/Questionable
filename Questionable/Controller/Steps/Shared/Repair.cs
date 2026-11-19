using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Questionable.Controller.Steps.Movement;
using static Questionable.Controller.Steps.ITaskExecutor;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller.Steps.Shared;

internal static class Repair
{
    private const uint RepairAction = 6;
    private const float ConditionPerPercent = 300f;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);
    private static DateTime _retryAt = DateTime.MinValue;

    // Quest dialogue and yes/no handling stay out of the repair window while this is set.
    internal static bool Active { get; private set; }

    internal sealed record Mender(uint NpcId, uint TerritoryId, Vector3 Position);

    // Grand Company headquarters menders, the same ones AutoDuty uses.
    internal static readonly Mender[] Menders =
    [
        new(1003251, 128, new(17.715698f, 40.200005f, 3.9520264f)), // Maelstrom, Limsa Lominsa
        new(1000394, 132, new(24.826416f, -8f, 93.18677f)), // Twin Adder, Gridania
        new(1004416, 130, new(32.85266f, 6.999999f, -81.31531f)), // Immortal Flames, Ul'dah
    ];

    // Grand Companies and starting cities share the order 1 Limsa, 2 Gridania, 3 Ul'dah.
    internal static Mender ChooseMender(byte grandCompany, byte startTown) =>
        Menders[Math.Clamp((grandCompany is >= 1 and <= 3 ? grandCompany : startTown) - 1, 0, Menders.Length - 1)];

    internal static bool IsWorn(ushort condition, int belowPercent) => condition / ConditionPerPercent < belowPercent;

    private static unsafe List<uint> WornItems(int belowPercent)
    {
        InventoryContainer* equipped = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        List<uint> worn = [];
        if (equipped == null)
            return worn;

        for (int i = 0; i < equipped->Size; i++)
        {
            InventoryItem item = equipped->Items[i];
            if (item.ItemId != 0 && IsWorn(item.Condition, belowPercent))
                worn.Add(item.ItemId);
        }

        return worn;
    }

    // Same rule as Artisan and AutoDuty: the repair class within 10 levels of the item and that Dark Matter grade or better.
    private static unsafe bool CanSelfRepair(IDataManager dataManager, uint itemId)
    {
        if (dataManager.GetExcelSheet<Item>().GetRowOrDefault(itemId) is not { } item ||
            item.ClassJobRepair.ValueNullable is not { } repairJob ||
            item.ItemRepair.ValueNullable?.Item.RowId is not { } darkMatter)
            return false;

        InventoryManager* inventory = InventoryManager.Instance();
        if (!dataManager.GetExcelSheet<ItemRepairResource>()
                .Any(x => x.Item.RowId >= darkMatter && inventory->GetInventoryItemCount(x.Item.RowId) > 0))
            return false;

        short level = PlayerState.Instance()->ClassJobLevels[repairJob.ExpArrayIndex];
        return level >= Math.Max(item.LevelEquip - 10, 1);
    }

    internal sealed class Factory(
        Configuration configuration,
        QuestFunctions questFunctions,
        GameFunctions gameFunctions,
        IQuestGameState gameState,
        ICondition condition,
        IDataManager dataManager,
        QuestPriorityManager priorityManager,
        ILogger<Factory> logger) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            int belowPercent = configuration.General.RepairBelowPercent;
            if (!configuration.General.RepairGear || DateTime.Now < _retryAt ||
                !BetweenQuests.CanDetour(quest, step, questFunctions, condition, gameFunctions, priorityManager))
                return [];

            List<uint> worn = WornItems(belowPercent);
            if (worn.Count == 0)
                return [];

            if (worn.All(x => CanSelfRepair(dataManager, x)))
            {
                logger.LogInformation("Repairing {Count} worn items with Dark Matter", worn.Count);
                return [new Take(0, belowPercent), new Done()];
            }

            Mender mender = ChooseMender(gameState.GrandCompany, gameState.StartTown);
            logger.LogInformation("Repairing {Count} worn items at mender {NpcId}", worn.Count, mender.NpcId);
            return
            [
                new Travel.Task(mender.TerritoryId, mender.Position, mender.NpcId),
                new Take(mender.NpcId, belowPercent),
                new Done(),
            ];
        }
    }

    // NpcId 0 repairs with Dark Matter.
    internal sealed record Take(uint NpcId, int BelowPercent) : ITask
    {
        public override string ToString() => NpcId == 0 ? "SelfRepair" : $"Repair({NpcId})";
    }

    internal sealed class TakeExecutor(GameFunctions gameFunctions, ICondition condition, ILogger<TakeExecutor> logger)
        : TaskExecutor<Take>, IStoppable
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
            if (WornItems(Task.BelowPercent).Count == 0)
            {
                logger.LogInformation("Gear repaired");
                Finish();
                return ETaskResult.TaskComplete;
            }

            if (DateTime.Now > _giveUpAt)
            {
                Finish();
                throw new TaskException($"the gear wasn't repaired within {GiveUpAfter.TotalSeconds} seconds");
            }

            if (DateTime.Now < _nextTry)
                return ETaskResult.StillRunning;
            _nextTry = DateTime.Now.AddSeconds(1);

            if (GenericHelpers.TryGetAddonMaster<AddonMaster.SelectYesno>(out var yesno) && yesno.IsAddonReady)
            {
                yesno.Yes();
                return ETaskResult.StillRunning;
            }

            if (GenericHelpers.TryGetAddonMaster<AddonMaster.Repair>(out var repair) && repair.IsAddonReady)
            {
                repair.RepairAll();
                return ETaskResult.StillRunning;
            }

            if (condition[ConditionFlag.Mounted])
            {
                gameFunctions.Unmount();
                return ETaskResult.StillRunning;
            }

            if (Task.NpcId == 0)
            {
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, RepairAction);
                _nextTry = DateTime.Now.AddSeconds(2);
                return ETaskResult.StillRunning;
            }

            // The headquarters menders list "Repair" first.
            if (GenericHelpers.TryGetAddonMaster<AddonMaster.SelectIconString>(out var iconMenu) && iconMenu.IsAddonReady)
            {
                iconMenu.Entries[0].Select();
                return ETaskResult.StillRunning;
            }

            if (condition[ConditionFlag.OccupiedInEvent] || condition[ConditionFlag.OccupiedInQuestEvent])
                return ETaskResult.StillRunning;

            if (gameFunctions.FindObjectByDataId(Task.NpcId) is { } mender)
            {
                gameFunctions.InteractWith(mender);
                _nextTry = DateTime.Now.AddSeconds(2);
            }

            return ETaskResult.StillRunning;
        }

        public void StopNow() => Finish();

        private static unsafe void Finish()
        {
            Active = false;
            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("Repair", out var repair) && repair->IsVisible)
                repair->Close(true);
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record Done : IOptionalGroupEnd
    {
        public bool RetryStep => true;

        public void OnFailed(Exception exception)
        {
            Active = false;
            _retryAt = DateTime.Now + RetryAfter;
            Svc.Chat.Print(_LF("Couldn't repair your gear: {0}. Carrying on; trying again in 30 minutes.",
                exception.Message.TrimEnd('.')), CommandHandler.MessageTag, CommandHandler.TagColor);
        }

        public override string ToString() => "RepairDone";
    }

    internal sealed class DoneExecutor : TaskExecutor<Done>
    {
        protected override bool Start() => true;
        public override ETaskResult Update() => ETaskResult.RetryStep;
        public override bool ShouldInterruptOnDamage() => false;
    }
}
