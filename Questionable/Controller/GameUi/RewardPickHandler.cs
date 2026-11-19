using System.Runtime.InteropServices;
using Dalamud.Game;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.ExcelServices;
using ECommons.UIHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using ERewardRule = Questionable.Configuration.ERewardRule;

namespace Questionable.Controller.GameUi;

[RegisterSingleton]
internal sealed unsafe class RewardPickHandler : IDisposable
{
    internal static readonly ERewardRule[] DefaultOrder = Enum.GetValues<ERewardRule>();

    // AtkComponentJournalCanvas.ReceiveEvent; picking reward i is event 9 with 7 + i (credit: TextAdvance's PickRewardItemUnsafe).
    private const string CanvasReceiveEvent = "48 89 5C 24 ?? 48 89 74 24 ?? 4C 89 4C 24 ?? 55";
    private delegate nint CanvasReceiveEventDelegate(nint canvas, ushort eventType, int eventParam, void* inputData, void* eventData);

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly QuestController _questController;
    private readonly Configuration _configuration;
    private readonly IDataManager _dataManager;
    private readonly ISigScanner _sigScanner;
    private readonly ILogger<RewardPickHandler> _logger;
    private CanvasReceiveEventDelegate? _receiveEvent;
    private HashSet<uint>? _allaganPieces;

    public RewardPickHandler(IAddonLifecycle addonLifecycle, QuestController questController, Configuration configuration,
        IDataManager dataManager, ISigScanner sigScanner, ILogger<RewardPickHandler> logger)
    {
        _addonLifecycle = addonLifecycle;
        _questController = questController;
        _configuration = configuration;
        _dataManager = dataManager;
        _sigScanner = sigScanner;
        _logger = logger;
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "JournalResult", OnPostSetup);
    }

    public void Dispose() => _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "JournalResult", OnPostSetup);

    internal sealed record Option(uint ItemId, bool UnownedCollectible, int UpgradeGain, bool Coffer, bool AllaganPiece,
        uint VendorValue);

    internal static int Choose(IReadOnlyList<Option> options, IEnumerable<ERewardRule> order, IReadOnlySet<ERewardRule> disabled)
    {
        foreach (ERewardRule rule in order.Distinct().Concat(DefaultOrder).Distinct().Where(x => !disabled.Contains(x)))
        {
            int pick = rule switch
            {
                ERewardRule.UnownedCollectible => FirstIndex(options, x => x.UnownedCollectible),
                ERewardRule.GearUpgrade => BestIndex(options, x => x.UpgradeGain),
                ERewardRule.Coffer => FirstIndex(options, x => x.Coffer),
                ERewardRule.AllaganPieces => BestIndex(options, x => x.AllaganPiece ? (int)x.VendorValue : 0),
                ERewardRule.VendorValue => BestIndex(options, x => (int)x.VendorValue),
                _ => -1,
            };
            if (pick >= 0)
                return pick;
        }

        return Math.Max(BestIndex(options, x => (int)x.VendorValue), 0);
    }

    private static int FirstIndex(IReadOnlyList<Option> options, Func<Option, bool> match)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (match(options[i]))
                return i;
        }

        return -1;
    }

    private static int BestIndex(IReadOnlyList<Option> options, Func<Option, int> score)
    {
        int best = -1, bestScore = 0;
        for (int i = 0; i < options.Count; i++)
        {
            int value = score(options[i]);
            if (value > bestScore)
                (best, bestScore) = (i, value);
        }

        return best;
    }

    private bool ShouldPick =>
        _configuration.General.ConfigureTextAdvance &&
        (_questController.IsRunning || _questController.AutomationType != QuestController.EAutomationType.Manual);

    private void OnPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!ShouldPick)
            return;

        AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
        List<(uint ItemId, uint Amount)> rewards = new JournalResultReader(addon).OptionalRewards
            .Select(x => (x.ItemId, x.Amount))
            .ToList();
        if (rewards.Count == 0)
            return;

        if (addon->UldManager.NodeListCount <= 7 || addon->UldManager.NodeList[7] == null)
        {
            _logger.LogWarning("Reward window has no reward canvas, leaving the choice to the player");
            return;
        }

        List<Option> options = rewards.Select(x => Describe(x.ItemId, x.Amount)).ToList();
        int index = Choose(options, _configuration.General.RewardOrder, _configuration.General.RewardRulesOff);
        _logger.LogInformation("Picking reward {Index} ({ItemId}) of {Options}", index, options[index].ItemId,
            string.Join(", ", options.Select(x => x.ItemId)));

        nint canvas = (nint)((AtkComponentNode*)addon->UldManager.NodeList[7])->Component;
        _receiveEvent ??= Marshal.GetDelegateForFunctionPointer<CanvasReceiveEventDelegate>(_sigScanner.ScanText(CanvasReceiveEvent));
        byte* inputData = stackalloc byte[100];
        byte* eventData = stackalloc byte[50];
        _receiveEvent(canvas, 9, 7 + index, inputData, eventData);
    }

    private Option Describe(uint itemId, uint amount)
    {
        if (_dataManager.GetExcelSheet<Item>().GetRowOrDefault(itemId) is not { } item)
            return new(itemId, false, 0, false, false, 0);

        Job job = (Job)PlayerState.Instance()->CurrentClassJobId;
        bool forJob = item.ClassJobCategory.RowId == 0 || item.ClassJobCategory.Value.IsJobInCategory(job);
        bool collectible = ItemReward.Create(item, new QuestId(0)) is
        {
            Type: EItemRewardType.Mount or EItemRewardType.Minion or EItemRewardType.OrchestrionRoll or
            EItemRewardType.TripleTriadCard or EItemRewardType.FashionAccessory
        } reward && !reward.IsUnlocked();
        return new(itemId, collectible, forJob ? UpgradeGain(item) : 0, forJob && ItemReward.IsValidCoffer(item),
            AllaganPieces().Contains(itemId), item.PriceLow * Math.Max(amount, 1));
    }

    private static int UpgradeGain(Item item)
    {
        if (item.EquipSlotCategory.ValueNullable is not { } slot || item.LevelEquip > PlayerState.Instance()->CurrentLevel)
            return 0;

        int[] slots = slot switch
        {
            { MainHand: > 0 } => [0],
            { OffHand: > 0 } => [1],
            { Head: > 0 } => [2],
            { Body: > 0 } => [3],
            { Gloves: > 0 } => [4],
            { Legs: > 0 } => [6],
            { Feet: > 0 } => [7],
            { Ears: > 0 } => [8],
            { Neck: > 0 } => [9],
            { Wrists: > 0 } => [10],
            { FingerL: > 0 } or { FingerR: > 0 } => [11, 12],
            _ => [],
        };
        if (slots.Length == 0)
            return 0;

        InventoryContainer* equipped = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipped == null)
            return 0;

        int worn = slots.Min(i => EquippedLevel(equipped, i));
        return Math.Max((int)item.LevelItem.RowId - worn, 0);
    }

    private static int EquippedLevel(InventoryContainer* equipped, int slot)
    {
        uint itemId = equipped->Items[slot].ItemId;
        return itemId == 0 ? 0 : (int)(Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.LevelItem.RowId ?? 0);
    }

    // Allagan tin, silver, gold and platinum pieces: items only worth their sale price.
    private HashSet<uint> AllaganPieces() => _allaganPieces ??= _dataManager.GetExcelSheet<Item>(ClientLanguage.English)
        .Where(x => x.Name.ExtractText() is var name && name.StartsWith("Allagan ", StringComparison.Ordinal) &&
                    name.EndsWith(" Piece", StringComparison.Ordinal))
        .Select(x => x.RowId)
        .ToHashSet();

    // Same layout TextAdvance reads: up to 5 optional rewards from atk value 82 on.
    private sealed class JournalResultReader(AtkUnitBase* addon) : AtkReader(addon)
    {
        public List<Reward> OptionalRewards => Loop<Reward>(82, 1, 5).Where(x => x.ItemId != 0).ToList();

        public sealed class Reward(nint addon, int offset) : AtkReader(addon, offset)
        {
            public uint ItemId => (ReadUInt(0) ?? 0) % 1_000_000;
            public uint Amount => ReadUInt(11) ?? 0;
        }
    }
}
