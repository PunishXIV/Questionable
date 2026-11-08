using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace Questionable.Controller.GameUi;

[RegisterSingleton]
internal sealed class CraftworksSupplyController : IDisposable
{
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly Configuration _configuration;
    private readonly IFramework _framework;
    private readonly GameGuiAdapter _gameGui;
    private readonly ILogger<CraftworksSupplyController> _logger;
    private readonly QuestController _questController;

    public CraftworksSupplyController(QuestController questController, IAddonLifecycle addonLifecycle,
        GameGuiAdapter gameGui, IFramework framework, Configuration configuration,
        ILogger<CraftworksSupplyController> logger)
    {
        _configuration = configuration;
        _questController = questController;
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _framework = framework;
        _logger = logger;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "ContextIconMenu", ContextIconMenuPostOpen);
        _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, "ContextIconMenu", ContextIconMenuPostOpen);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "BankaCraftworksSupply",
            SupplyPostSetup);
    }

    private bool ShouldHandleUi => _questController.IsRunning;

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "BankaCraftworksSupply",
            SupplyPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, "ContextIconMenu", ContextIconMenuPostOpen);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "ContextIconMenu", ContextIconMenuPostOpen);
    }

    private unsafe void SupplyPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!ShouldHandleUi)
            return;

        AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
        HandleSupply(addon);
    }

    private unsafe void HandleSupply()
    {
        if (_gameGui.TryGetAddonByName("BankaCraftworksSupply", out AtkUnitBase* addon))
            HandleSupply(addon);
    }

    private unsafe void HandleSupply(AtkUnitBase* addon)
    {
        AddonMaster.BankaCraftworksSupply master = new(addon);
        if (master.FirstUnfilledSlot is { } slot)
        {
            // TryHandOver opens the item-pick context menu, which ContextIconMenuPostOpen handles.
            _logger.LogInformation("Selecting an item for slot {Slot}", slot);
            master.TryHandOver(slot);
        }
        else if (_configuration.Advanced.SkipCraftworksDelivery)
            _logger.LogInformation("All slots filled; not delivering (testing option is on)");
        else
        {
            _logger.LogInformation("Confirming turn-in");
            master.Deliver();
        }
    }

    // TODO: confirm on a real delivery that the item is picked with the cursor outside the game window.
    private unsafe void ContextIconMenuPostOpen(AddonEvent type, AddonArgs args)
    {
        if (!ShouldHandleUi)
            return;
        
        _framework.RunOnTick(() =>
        {
            if (ShouldHandleUi && _gameGui.TryGetAddonByName("ContextIconMenu", out AtkUnitBase* addon))
                PickSupplyItem((AddonContextIconMenu*)addon);
        });
    }

    private unsafe void PickSupplyItem(AddonContextIconMenu* addonContextIconMenu)
    {
        if (!addonContextIconMenu->IsVisible)
            return;

        ushort parentId = addonContextIconMenu->BlockedParentId;
        if (parentId == 0)
            return;

        AtkUnitBase* parentAddon = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonById(parentId);
        if (string.Equals(parentAddon->NameString, "BankaCraftworksSupply", StringComparison.Ordinal))
        {
            _logger.LogInformation("Picking item for {AddonName}", parentAddon->NameString);
            AtkValue* selectSlot = stackalloc AtkValue[]
            {
                new() { Type = AtkValueType.Int, Int = 0 },
                new() { Type = AtkValueType.Int, Int = 0 /* slot */ },
                new() { Type = AtkValueType.UInt, UInt = 20802 /* probably the item's icon */ },
                new() { Type = AtkValueType.UInt, UInt = 0 },
                new() { Type = 0, Int = 0 }
            };
            addonContextIconMenu->FireCallback(5, selectSlot);
            addonContextIconMenu->Close(fireCallback: true);

            if (parentAddon->NameString == "BankaCraftworksSupply")
                _framework.RunOnTick(HandleSupply, TimeSpan.FromMilliseconds(50));
        }
        else
            _logger.LogTrace("Ignoring contextmenu event for {AddonName}", parentAddon->NameString);
    }
}
