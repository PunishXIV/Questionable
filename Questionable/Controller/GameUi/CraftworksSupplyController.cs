using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace Questionable.Controller.GameUi;

internal sealed class CraftworksSupplyController : IDisposable
{
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IFramework _framework;
    private readonly IGameGuiAdapter _gameGui;
    private readonly ILogger<CraftworksSupplyController> _logger;
    private readonly QuestController _questController;

    public CraftworksSupplyController(QuestController questController, IAddonLifecycle addonLifecycle,
        IGameGuiAdapter gameGui, IFramework framework, ILogger<CraftworksSupplyController> logger)
    {
        _questController = questController;
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _framework = framework;
        _logger = logger;

        _addonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, "ContextIconMenu", ContextIconMenuPostReceiveEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "BankaCraftworksSupply",
            BankaCraftworksSupplyPostUpdate);
    }

    private bool ShouldHandleUiInteractions => _questController.IsRunning;

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "BankaCraftworksSupply",
            BankaCraftworksSupplyPostUpdate);
        _addonLifecycle.UnregisterListener(AddonEvent.PostReceiveEvent, "ContextIconMenu", ContextIconMenuPostReceiveEvent);
    }

    private unsafe void BankaCraftworksSupplyPostUpdate(AddonEvent type, AddonArgs args)
    {
        if (!ShouldHandleUiInteractions)
            return;

        AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
        InteractWithBankaCraftworksSupply(addon);
    }

    private unsafe void InteractWithBankaCraftworksSupply()
    {
        if (_gameGui.TryGetAddonByName("BankaCraftworksSupply", out AtkUnitBase* addon))
            InteractWithBankaCraftworksSupply(addon);
    }

    private unsafe void InteractWithBankaCraftworksSupply(AtkUnitBase* addon)
    {
        AddonMaster.BankaCraftworksSupply master = new(addon);
        if (master.FirstUnfilledSlot is { } slot)
        {
            // TryHandOver opens the item-pick context menu, which ContextIconMenuPostReceiveEvent handles.
            _logger.LogInformation("Selecting an item for slot {Slot}", slot);
            master.TryHandOver(slot);
        }
        else
        {
            _logger.LogInformation("Confirming turn-in");
            master.Deliver();
        }
    }

    // FIXME: This seems to not work if the mouse isn't over the FFXIV window?
    private unsafe void ContextIconMenuPostReceiveEvent(AddonEvent type, AddonArgs args)
    {
        if (!ShouldHandleUiInteractions)
            return;

        AddonContextIconMenu* addonContextIconMenu = (AddonContextIconMenu*)args.Addon.Address;
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
                _framework.RunOnTick(InteractWithBankaCraftworksSupply, TimeSpan.FromMilliseconds(50));
        }
        else
            _logger.LogTrace("Ignoring contextmenu event for {AddonName}", parentAddon->NameString);
    }
}
