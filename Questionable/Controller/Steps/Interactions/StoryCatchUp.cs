using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Questionable.Controller.Steps.Shared;
using static Questionable.Controller.Steps.ITaskExecutor;

namespace Questionable.Controller.Steps.Interactions;

[RegisterSingleton]
internal sealed class StoryCatchUp(
    AutoDutyProvider autoDuty,
    TerritoryData territoryData,
    TomestoneShopData tomestoneShopData,
    GearStatsCalculator gearStats,
    Configuration configuration,
    IDataManager dataManager,
    IChatGui chatGui,
    ILogger<StoryCatchUp> logger)
{
    private const int MaxRunsWithoutGain = 5;
    private const int EarSlot = 8, NeckSlot = 9, WristSlot = 10, RingSlot = 11, OtherRingSlot = 12;
    private readonly Dictionary<uint, int> _boughtAt = [];
    private readonly Dictionary<uint, (int Runs, int ItemLevel, int Tomestones)> _gearRuns = [];

    public bool Enabled => configuration.Duties.CatchUpForStory && configuration.Duties.RunInstancedContentWithAutoDuty;

    internal static TerritoryData.CfcData? PickDungeon(IEnumerable<TerritoryData.CfcData> duties, int playerLevel,
        Func<uint, bool> hasSupport, Func<uint, bool> hasPath) =>
        duties
            .Where(x => x.ContentType == EContentType.Dungeons && x.RequiredLevel > 0 && x.RequiredLevel <= playerLevel &&
                        hasSupport(x.Id))
            .OrderByDescending(x => x.RequiredLevel)
            .ThenByDescending(x => x.ClassJobLevelSync)
            .FirstOrDefault(x => hasPath(x.Id));

    internal sealed record Accessory(uint ItemId, int Slot, int ItemLevel);

    internal static List<uint> PickAccessories(IEnumerable<Accessory> offered, Func<int, int> wornLevel) =>
        offered.GroupBy(x => x.Slot)
            .Select(x => x.MaxBy(y => y.ItemLevel)!)
            .Where(x => x.ItemLevel > wornLevel(x.Slot))
            .Select(x => x.ItemId)
            .ToList();

    internal static bool Gained(int itemLevel, int lastItemLevel, bool upgradesLeft, int tomestones, int lastTomestones) =>
        itemLevel > lastItemLevel || upgradesLeft && tomestones > lastTomestones;

    public ITask? LevelRun(string reason, int playerLevel)
    {
        if (PickDungeon(territoryData.AllCfcs, playerLevel, territoryData.HasDutySupport, autoDuty.HasPath) is not { } dungeon)
        {
            logger.LogInformation("No dungeon to level in at level {Level}", playerLevel);
            return null;
        }

        chatGui.Print(_LF("{0}: running '{1}' with AutoDuty to catch up.", reason, dungeon.Name),
            CommandHandler.MessageTag, CommandHandler.TagColor);
        return new Duty.StartAutoDutyTask(dungeon.Id, Duty.Mode.Support);
    }

    public List<ITask>? GearUp(TerritoryData.CfcData duty, int playerLevel, short itemLevel)
    {
        string reason = _LF("'{0}' needs item level {1} (you have {2})", duty.Name, duty.RequiredItemLevel, itemLevel);
        int tomestones = Tomestones();
        List<ITask> buys = AccessoryBuys(playerLevel);
        // Tries again only once the tomestones changed, so an upgrade it can't afford yet doesn't loop.
        if (buys.Count > 0 && _boughtAt.GetValueOrDefault(duty.Id, -1) != tomestones)
        {
            _boughtAt[duty.Id] = tomestones;
            chatGui.Print(_LF("{0}: buying {1} accessories with tomestones.", reason, buys.Count),
                CommandHandler.MessageTag, CommandHandler.TagColor);
            return [.. buys, new EquipRecommended.EquipTask(), new Replan()];
        }

        (int runs, int lastItemLevel, int lastTomestones) = _gearRuns.GetValueOrDefault(duty.Id, (0, itemLevel, tomestones));
        if (Gained(itemLevel, lastItemLevel, buys.Count > 0, tomestones, lastTomestones))
            runs = 0;
        if (runs >= MaxRunsWithoutGain)
            return null;

        if (LevelRun(reason, playerLevel) is not { } run)
            return null;

        _gearRuns[duty.Id] = (runs + 1, itemLevel, tomestones);
        return [run, new EquipRecommended.EquipTask(), new Replan()];
    }

    public short? ItemLevel() => Duty.StartAutoDutyExecutor.GetItemLevel(gearStats);

    private unsafe int Tomestones()
    {
        InventoryManager* inventory = InventoryManager.Instance();
        return inventory == null
            ? 0
            : dataManager.GetExcelSheet<TomestonesItem>()
                .Where(x => x.Item.RowId != 0)
                .Sum(x => inventory->GetInventoryItemCount(x.Item.RowId));
    }

    private unsafe List<ITask> AccessoryBuys(int playerLevel)
    {
        Job job = (Job)PlayerState.Instance()->CurrentClassJobId;
        InventoryContainer* equipped = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipped == null)
            return [];

        var items = dataManager.GetExcelSheet<Item>();
        List<Accessory> offered = [];
        foreach (uint itemId in tomestoneShopData.Items)
        {
            if (items.GetRowOrDefault(itemId) is not { } item || item.LevelEquip > playerLevel ||
                item.ClassJobCategory.RowId == 0 || !item.ClassJobCategory.Value.IsJobInCategory(job) ||
                AccessorySlot(item) is not { } slot)
                continue;
            offered.Add(new(itemId, slot, (int)item.LevelItem.RowId));
        }

        int WornLevel(int slot)
        {
            int Level(int i) => equipped->Items[i].ItemId is var id and not 0
                ? (int)(items.GetRowOrDefault(id)?.LevelItem.RowId ?? 0)
                : 0;
            return slot == RingSlot ? Math.Min(Level(RingSlot), Level(OtherRingSlot)) : Level(slot);
        }

        return [.. PickAccessories(offered, WornLevel)
            .Select(x => new Purchase.BuyTask(x, 1, EItemQuality.Any, Purchase.Tomestone, Optional: true))];
    }

    private static int? AccessorySlot(Item item) => item.EquipSlotCategory.ValueNullable switch
    {
        { Ears: > 0 } => EarSlot,
        { Neck: > 0 } => NeckSlot,
        { Wrists: > 0 } => WristSlot,
        { FingerL: > 0 } or { FingerR: > 0 } => RingSlot,
        _ => null,
    };

    internal sealed record Replan : ITask
    {
        public override string ToString() => "CatchUpReplan";
    }

    internal sealed class ReplanExecutor : TaskExecutor<Replan>
    {
        protected override bool Start() => true;
        public override ETaskResult Update() => ETaskResult.RetryStep;
        public override bool ShouldInterruptOnDamage() => false;
    }
}
