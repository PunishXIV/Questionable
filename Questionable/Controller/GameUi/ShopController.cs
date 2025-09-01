using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.GameUI;
using LLib.Shop;
using LLib.Shop.Model;
using Microsoft.Extensions.Logging;
using Questionable.Model.Questing;
using System;
using System.Linq;
using System.Numerics;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace Questionable.Controller.GameUi;

internal sealed class ShopController : IDisposable, IShopWindow
{
    private readonly QuestController _questController;
    private readonly IGameGui _gameGui;
    private readonly IFramework _framework;
    private readonly RegularShopBase _shop;
    private readonly ILogger<ShopController> _logger;

    public ShopController(QuestController questController, IGameGui gameGui, IAddonLifecycle addonLifecycle,
        IFramework framework, ILogger<ShopController> logger, IPluginLog pluginLog)
    {
        _questController = questController;
        _gameGui = gameGui;
        _framework = framework;
        _shop = new RegularShopBase(this, "Shop", pluginLog, gameGui, addonLifecycle);
        _logger = logger;

        _framework.Update += FrameworkUpdate;
    }

    public bool IsEnabled => _questController.IsRunning;
    public bool IsOpen { get; set; }
    public bool IsAutoBuyEnabled => _shop.AutoBuyEnabled;

    public bool IsAwaitingYesNo
    {
        get { return _shop.IsAwaitingYesNo; }
        set { _shop.IsAwaitingYesNo = value; }
    }

    public Vector2? Position { get; set; } // actual implementation doesn't matter, not a real window

    public void Dispose()
    {
        _framework.Update -= FrameworkUpdate;
        _shop.Dispose();
    }

    private void FrameworkUpdate(IFramework framework)
    {
        if (IsOpen && _shop.ItemForSale != null)
        {
            if (_shop.PurchaseState != null)
            {
                _shop.HandleNextPurchaseStep();
            }
            else
            {
                var currentStep = FindCurrentStep();
                if (currentStep == null || currentStep.InteractionType != EInteractionType.PurchaseItem)
                    return;

                int missingItems = Math.Max(0,
                    currentStep.ItemCount.GetValueOrDefault() - (int)_shop.ItemForSale.OwnedItems);
                int toPurchase = Math.Min(_shop.GetMaxItemsToPurchase(), missingItems);
                if (toPurchase > 0)
                {
                    _logger.LogDebug("Auto-buying {MissingItems} {ItemName}", missingItems, _shop.ItemForSale.ItemName);
                    _shop.StartAutoPurchase(missingItems);
                    _shop.HandleNextPurchaseStep();
                }
                else
                    _shop.CancelAutoPurchase();
            }
        }
    }

    public int GetCurrencyCount() => _shop.GetItemCount(1); // TODO: support other currencies

    private QuestStep? FindCurrentStep()
    {
        var currentQuest = _questController.CurrentQuest;
        QuestSequence? currentSequence = currentQuest?.Quest.FindSequence(currentQuest.Sequence);
        return currentSequence?.FindStep(currentQuest?.Step ?? 0);
    }

    public unsafe void UpdateShopStock(AtkUnitBase* addon)
    {
        var currentStep = FindCurrentStep();
        if (currentStep == null || currentStep.InteractionType != EInteractionType.PurchaseItem)
        {
            _shop.ItemForSale = null;
            return;
        }

        if (addon->AtkValuesCount != 625)
        {
            _logger.LogError("Unexpected amount of atkvalues for Shop addon ({AtkValueCount})", addon->AtkValuesCount);
            _shop.ItemForSale = null;
            return;
        }

        var atkValues = addon->AtkValues;

        // Check if on 'Current Stock' tab?
        if (atkValues[0].UInt != 0)
        {
            _shop.ItemForSale = null;
            return;
        }

        uint itemCount = atkValues[2].UInt;
        if (itemCount == 0)
        {
            _shop.ItemForSale = null;
            return;
        }

        _shop.ItemForSale = Enumerable.Range(0, (int)itemCount)
            .Select(i => new ItemForSale
            {
                Position = i,
                ItemName = atkValues[14 + i].ReadAtkString(),
                Price = atkValues[75 + i].UInt,
                OwnedItems = atkValues[136 + i].UInt,
                ItemId = atkValues[441 + i].UInt,
            })
            .FirstOrDefault(x => x.ItemId == currentStep.ItemId);
    }

    public unsafe void TriggerPurchase(AtkUnitBase* addonShop, int buyNow)
    {
        var buyItem = stackalloc AtkValue[]
        {
            new() { Type = ValueType.Int, Int = 0 },
            new() { Type = ValueType.Int, Int = _shop.ItemForSale!.Position },
            new() { Type = ValueType.Int, Int = buyNow },
            new() { Type = 0, Int = 0 }
        };
        addonShop->FireCallback(4, buyItem);
    }

    public void SaveExternalPluginState()
    {
    }

    public unsafe void RestoreExternalPluginState()
    {
        if (_gameGui.TryGetAddonByName("Shop", out AtkUnitBase* addonShop))
            addonShop->FireCallbackInt(-1);
    }
}
