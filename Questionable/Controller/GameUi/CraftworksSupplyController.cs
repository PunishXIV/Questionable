using Callback = ECommons.Automation.Callback;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace Questionable.Controller.GameUi;

[RegisterSingleton]
internal sealed class CraftworksSupplyController : IDisposable
{
    private static readonly (string Addon, AgentId Agent, int ItemValue)[] Windows =
    [
        ("HugeCraftworksSupply", AgentId.HugeCraftworksSupply, 9),
        ("SharlayanCraftworksSupply", AgentId.SharlayanCraftworksSupply, 8),
        ("BankaCraftworksSupply", AgentId.BankaCraftworksSupply, 8),
    ];

    private static readonly string[] HelpAddons = ["HowTo", "HowToNotice"];
    private static readonly TimeSpan DeliverWait = TimeSpan.FromSeconds(5);

    private readonly QuestController _questController;
    private readonly GameGuiAdapter _gameGui;
    private readonly IFramework _framework;
    private readonly ILogger<CraftworksSupplyController> _logger;
    private readonly HashSet<(InventoryType, short)> _handed = [];
    private DateTime _deliveredAt;
    private int _tries;

    public CraftworksSupplyController(QuestController questController, GameGuiAdapter gameGui, IFramework framework,
        ILogger<CraftworksSupplyController> logger)
    {
        _questController = questController;
        _gameGui = gameGui;
        _framework = framework;
        _logger = logger;
        _framework.Update += Update;
    }

    internal readonly record struct Candidate(InventoryType Container, short Slot, bool Hq, ushort Collectability);

    internal static Candidate? Pick(IEnumerable<Candidate> items, IReadOnlySet<(InventoryType, short)> handed) =>
        items.Where(x => !handed.Contains((x.Container, x.Slot)))
            .OrderByDescending(x => x.Hq)
            .ThenByDescending(x => x.Collectability)
            .Cast<Candidate?>()
            .FirstOrDefault();

    private unsafe void Update(IFramework framework)
    {
        if (!_questController.IsRunning || !EzThrottler.Throttle("CraftworksSupply", 700))
            return;

        // The first delivery opens the game's help on top, and a hand-over made under it is lost (4158).
        foreach (string help in HelpAddons)
        {
            if (_gameGui.TryGetAddonByName(help, out AtkUnitBase* helpAddon) && helpAddon->IsVisible)
                return;
        }

        if (TryGetReady("HugeCraftworksSupplyResult", out AtkUnitBase* result))
        {
            Callback.Fire(result, true, 0);
            return;
        }

        foreach (var window in Windows)
        {
            if (TryGetReady(window.Addon, out AtkUnitBase* addon))
            {
                Handle(window.Addon, window.Agent, window.ItemValue, addon);
                return;
            }
        }

        _handed.Clear();
        _deliveredAt = default;
        _tries = 0;
    }

    private unsafe bool TryGetReady(string name, out AtkUnitBase* addon) =>
        _gameGui.TryGetAddonByName(name, out addon) && AddonUtils.IsAddonReady(addon) && addon->IsVisible;

    private unsafe void Handle(string name, AgentId agentId, int itemValue, AtkUnitBase* addon)
    {
        if (TryGetReady("InputNumeric", out AtkUnitBase* numeric))
        {
            AddonMaster.InputNumeric input = new(numeric);
            input.Ok((int)input.Max);
            return;
        }

        if (_deliveredAt != default)
        {
            if (DateTime.Now - _deliveredAt < DeliverWait)
                return;

            if (_tries >= 2)
            {
                if (_tries++ == 2)
                    _logger.LogWarning("{Addon} is still open after delivering twice; leaving it", name);
                return;
            }

            Deliver(name, addon);
            return;
        }

        if (addon->AtkValuesCount <= itemValue ||
            addon->AtkValues[itemValue].Type is not (AtkValueType.Int or AtkValueType.UInt))
            return;

        // Collectables come as id + 500000, HQ items as id + 1000000.
        uint itemId = addon->AtkValues[itemValue].UInt % 500_000;
        if (itemId == 0)
            return;

        if (Pick(Candidates(itemId), _handed) is { } item)
        {
            _handed.Add((item.Container, item.Slot));
            _logger.LogInformation("Handing over item {ItemId} from {Container}/{Slot} to {Addon}", itemId,
                item.Container, item.Slot, name);
            AgentInterface* agent = AgentModule.Instance()->GetAgentByInternalId(agentId);
            if (agent == null)
                return;

            // All three agents keep their InventoryContextEvent at +0x28 (constructors, game 2026.11.03).
            ((AgentInventoryContext.InventoryContextEvent*)((byte*)agent + 0x28))
                ->HandleCallback((uint)item.Slot, item.Container, 0, 0);
            return;
        }

        if (_handed.Count == 0)
        {
            if (EzThrottler.Throttle("CraftworksSupplyEmpty", 10_000))
                _logger.LogWarning("{Addon} wants item {ItemId}, but none is in the bags", name, itemId);
            return;
        }

        Deliver(name, addon);
    }

    private unsafe void Deliver(string name, AtkUnitBase* addon)
    {
        _logger.LogInformation("Delivering {Addon} ({Count} items handed over)", name, _handed.Count);
        Callback.Fire(addon, true, 0);
        _deliveredAt = DateTime.Now;
        _tries++;
    }

    private static unsafe List<Candidate> Candidates(uint itemId)
    {
        List<Candidate> candidates = [];
        InventoryManager* inventoryManager = InventoryManager.Instance();
        for (InventoryType type = InventoryType.Inventory1; type <= InventoryType.Inventory4; ++type)
        {
            InventoryContainer* container = inventoryManager->GetInventoryContainer(type);
            if (container == null)
                continue;

            for (int i = 0; i < container->Size; ++i)
            {
                InventoryItem* item = container->GetInventorySlot(i);
                if (item != null && item->ItemId == itemId)
                    candidates.Add(new(type, item->Slot, item->IsHighQuality(), item->SpiritbondOrCollectability));
            }
        }

        return candidates;
    }

    public void Dispose() => _framework.Update -= Update;
}
