using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.GameUI;
using Microsoft.Extensions.Logging;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace Questionable.Controller.GameUi;

internal sealed class CraftworksSupplyController : IDisposable
{
    private readonly QuestController _questController;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGui _gameGui;
    private readonly IFramework _framework;
    private readonly ILogger<CraftworksSupplyController> _logger;

    public CraftworksSupplyController(QuestController questController, IAddonLifecycle addonLifecycle,
        IGameGui gameGui, IFramework framework, ILogger<CraftworksSupplyController> logger)
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
        AtkValue* atkValues = addon->AtkValues;

        uint completedCount = atkValues[7].UInt;
        uint missingCount = 6 - completedCount;
        for (int slot = 0; slot < missingCount; ++slot)
        {
            if (atkValues[31 + slot].UInt != 0)
                continue;

            _logger.LogInformation("Selecting an item for slot {Slot}", slot);
            var selectSlot = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 2 },
                new() { Type = ValueType.Int, Int = slot /* slot */ },
            };
            addon->FireCallback(2, selectSlot);
            return;
        }

        // do turn-in if any item is provided
        if (atkValues[31].UInt != 0)
        {
            _logger.LogInformation("Confirming turn-in");
            addon->FireCallbackInt(0);
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

        ushort parentId = addonContextIconMenu->ContextMenuParentId;
        if (parentId == 0)
            return;

        AtkUnitBase* parentAddon = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonById(parentId);
        if (parentAddon->NameString is "BankaCraftworksSupply")
        {
            _logger.LogInformation("Picking item for {AddonName}", parentAddon->NameString);
            var selectSlot = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 0 },
                new() { Type = ValueType.Int, Int = 0 /* slot */ },
                new() { Type = ValueType.UInt, UInt = 20802 /* probably the item's icon */ },
                new() { Type = ValueType.UInt, UInt = 0 },
                new() { Type = 0, Int = 0 },
            };
            addonContextIconMenu->FireCallback(5, selectSlot);
            addonContextIconMenu->Close(true);

            if (parentAddon->NameString == "BankaCraftworksSupply")
                _framework.RunOnTick(InteractWithBankaCraftworksSupply, TimeSpan.FromMilliseconds(50));
        }
        else
            _logger.LogTrace("Ignoring contextmenu event for {AddonName}", parentAddon->NameString);
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "BankaCraftworksSupply",
            BankaCraftworksSupplyPostUpdate);
        _addonLifecycle.UnregisterListener(AddonEvent.PostReceiveEvent, "ContextIconMenu", ContextIconMenuPostReceiveEvent);
    }
}
