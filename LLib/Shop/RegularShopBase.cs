using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.GameUI;
using LLib.Shop.Model;

namespace LLib.Shop;

[SuppressMessage("ReSharper", "ClassCanBeSealed.Global")]
public class RegularShopBase
{
    private readonly IShopWindow _parentWindow;
    private readonly string _addonName;
    private readonly IPluginLog _pluginLog;
    private readonly IGameGui _gameGui;
    private readonly IAddonLifecycle _addonLifecycle;

    public RegularShopBase(IShopWindow parentWindow, string addonName, IPluginLog pluginLog, IGameGui gameGui, IAddonLifecycle addonLifecycle)
    {
        _parentWindow = parentWindow;
        _addonName = addonName;
        _pluginLog = pluginLog;
        _gameGui = gameGui;
        _addonLifecycle = addonLifecycle;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, _addonName, ShopPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, _addonName, ShopPreFinalize);
        _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, _addonName, ShopPostUpdate);
    }

    public ItemForSale? ItemForSale { get; set; }
    public PurchaseState? PurchaseState { get; private set; }
    public bool AutoBuyEnabled => PurchaseState != null;

    public bool IsAwaitingYesNo
    {
        get => PurchaseState?.IsAwaitingYesNo ?? false;
        set => PurchaseState!.IsAwaitingYesNo = value;
    }

    private unsafe void ShopPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!_parentWindow.IsEnabled)
        {
            ItemForSale = null;
            _parentWindow.IsOpen = false;
            return;
        }

        _parentWindow.UpdateShopStock((AtkUnitBase*)args.Addon.Address);
        PostUpdateShopStock();
        if (ItemForSale != null)
            _parentWindow.IsOpen = true;
    }

    private void ShopPreFinalize(AddonEvent type, AddonArgs args)
    {
        PurchaseState = null;
        _parentWindow.RestoreExternalPluginState();
        _parentWindow.IsOpen = false;
    }

    private unsafe void ShopPostUpdate(AddonEvent type, AddonArgs args)
    {
        if (!_parentWindow.IsEnabled)
        {
            ItemForSale = null;
            _parentWindow.IsOpen = false;
            return;
        }

        _parentWindow.UpdateShopStock((AtkUnitBase*)args.Addon.Address);
        PostUpdateShopStock();
        if (ItemForSale != null)
        {
            AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
            short x = 0, y = 0;
            addon->GetPosition(&x, &y);

            short width = 0, height = 0;
            addon->GetSize(&width, &height, true);
            x += width;

            if (_parentWindow.Position is {} position && ((short)position.X != x || (short)position.Y != y))
                _parentWindow.Position = new Vector2(x, y);

            _parentWindow.IsOpen = true;
        }
        else
            _parentWindow.IsOpen = false;
    }

    private void PostUpdateShopStock()
    {
        if (ItemForSale != null && PurchaseState != null)
        {
            int ownedItems = (int)ItemForSale.OwnedItems;
            if (PurchaseState.OwnedItems != ownedItems)
            {
                PurchaseState.OwnedItems = ownedItems;
                PurchaseState.NextStep = DateTime.Now.AddSeconds(0.25);
            }
        }
    }

    public unsafe int GetItemCount(uint itemId)
    {
        InventoryManager* inventoryManager = InventoryManager.Instance();
        return inventoryManager->GetInventoryItemCount(itemId, checkEquipped: false, checkArmory: false);
    }

    public int GetMaxItemsToPurchase()
    {
        if (ItemForSale == null)
            return 0;

        int currency = _parentWindow.GetCurrencyCount();
        return (int)(currency / ItemForSale!.Price);
    }

    public void CancelAutoPurchase()
    {
        PurchaseState = null;
        _parentWindow.RestoreExternalPluginState();
    }

    public void StartAutoPurchase(int toPurchase)
    {
        PurchaseState = new((int)ItemForSale!.OwnedItems + toPurchase, (int)ItemForSale.OwnedItems);
        _parentWindow.SaveExternalPluginState();
    }

    public unsafe void HandleNextPurchaseStep()
    {
        if (ItemForSale == null || PurchaseState == null)
            return;

        int maxStackSize = DetermineMaxStackSize(ItemForSale.ItemId);
        if (maxStackSize == 0 && !HasFreeInventorySlot())
        {
            _pluginLog.Warning($"No free inventory slots, can't buy more {ItemForSale.ItemName}");
            PurchaseState = null;
            _parentWindow.RestoreExternalPluginState();
        }
        else if (!PurchaseState.IsComplete)
        {
            if (PurchaseState.NextStep <= DateTime.Now &&
                _gameGui.TryGetAddonByName(_addonName, out AtkUnitBase* addonShop))
            {
                int buyNow = Math.Min(PurchaseState.ItemsLeftToBuy, maxStackSize);
                _pluginLog.Information($"Buying {buyNow}x {ItemForSale.ItemName}");

                _parentWindow.TriggerPurchase(addonShop, buyNow);

                PurchaseState.NextStep = DateTime.MaxValue;
                PurchaseState.IsAwaitingYesNo = true;
            }
        }
        else
        {
            _pluginLog.Information(
                $"Stopping item purchase (desired = {PurchaseState.DesiredItems}, owned = {PurchaseState.OwnedItems})");
            PurchaseState = null;
            _parentWindow.RestoreExternalPluginState();
        }
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, _addonName, ShopPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, _addonName, ShopPreFinalize);
        _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, _addonName, ShopPostUpdate);
    }


    public bool HasFreeInventorySlot() => CountFreeInventorySlots() > 0;

    public unsafe int CountFreeInventorySlots()
    {
        var inventoryManger = InventoryManager.Instance();
        if (inventoryManger == null)
            return 0;

        int count = 0;
        for (InventoryType t = InventoryType.Inventory1; t <= InventoryType.Inventory4; ++t)
        {
            var container = inventoryManger->GetInventoryContainer(t);
            for (int i = 0; i < container->Size; ++i)
            {
                var item = container->GetInventorySlot(i);
                if (item == null || item->ItemId == 0)
                    ++count;
            }
        }

        return count;
    }

    private unsafe int DetermineMaxStackSize(uint itemId)
    {
        var inventoryManger = InventoryManager.Instance();
        if (inventoryManger == null)
            return 0;

        int max = 0;
        for (InventoryType t = InventoryType.Inventory1; t <= InventoryType.Inventory4; ++t)
        {
            var container = inventoryManger->GetInventoryContainer(t);
            for (int i = 0; i < container->Size; ++i)
            {
                var item = container->GetInventorySlot(i);
                if (item == null || item->ItemId == 0)
                    return 99;

                if (item->ItemId == itemId)
                {
                    max += (999 - item->Quantity);
                    if (max >= 99)
                        break;
                }
            }
        }

        return Math.Min(99, max);
    }

    public unsafe int CountInventorySlotsWithCondition(uint itemId, Predicate<int> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return 0;

        int count = 0;
        for (InventoryType t = InventoryType.Inventory1; t <= InventoryType.Inventory4; ++t)
        {
            var container = inventoryManager->GetInventoryContainer(t);
            for (int i = 0; i < container->Size; ++i)
            {
                var item = container->GetInventorySlot(i);
                if (item == null || item->ItemId == 0)
                    continue;

                if (item->ItemId == itemId && predicate(item->Quantity))
                    ++count;
            }
        }

        return count;
    }
}
