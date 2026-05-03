using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.Shop;
using LLib.Shop.Model;
using Microsoft.Extensions.Logging;
using Questionable.Model.Questing;
using Questionable.Utils;
using System;
using System.Linq;
using System.Numerics;
namespace Questionable.Controller.GameUi;

internal sealed class ShopController : IDisposable, IShopWindow
{
    private readonly IFramework _framework;
    private readonly IGameGuiAdapter _gameGuiAdapter;
    private readonly ILogger<ShopController> _logger;
    private readonly QuestController _questController;
    // Intentionally retained integration boundary: shop automation still relies on LLib.Shop internals.
    private readonly RegularShopBase _shop;

    public ShopController(QuestController questController, IGameGui gameGui, IGameGuiAdapter gameGuiAdapter, IAddonLifecycle addonLifecycle,
        IFramework framework, ILogger<ShopController> logger, IPluginLog pluginLog)
    {
        _questController = questController;
        _gameGuiAdapter = gameGuiAdapter;
        _framework = framework;
        _shop = new(this, "Shop", pluginLog, gameGui, addonLifecycle);
        _logger = logger;

        _framework.Update += FrameworkUpdate;
    }
    public bool IsAutoBuyEnabled => _shop.AutoBuyEnabled;

    public bool IsAwaitingYesNo
    {
        get => _shop.IsAwaitingYesNo;
        set => _shop.IsAwaitingYesNo = value;
    }

    public void Dispose()
    {
        _framework.Update -= FrameworkUpdate;
        _shop.Dispose();
    }

    public bool IsEnabled => _questController.IsRunning;
    public bool IsOpen { get; set; }

    public Vector2? Position { get; set; } // actual implementation doesn't matter, not a real window

    public int GetCurrencyCount()
    {
        return _shop.GetItemCount(1);
        // TODO: support other currencies
    }

    public unsafe void UpdateShopStock(AtkUnitBase* addon)
    {
        QuestStep? currentStep = FindCurrentStep();
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

        AtkValue* atkValues = addon->AtkValues;

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
                ItemName = AtkValueAdapter.ReadString(atkValues[14 + i]),
                Price = atkValues[75 + i].UInt,
                OwnedItems = atkValues[136 + i].UInt,
                ItemId = atkValues[441 + i].UInt
            })
            .FirstOrDefault(x => x.ItemId == currentStep.ItemId);
    }

    public unsafe void TriggerPurchase(AtkUnitBase* addonShop, int buyNow)
    {
        AtkValue* buyItem = stackalloc AtkValue[]
        {
            new() { Type = AtkValueType.Int, Int = 0 },
            new() { Type = AtkValueType.Int, Int = _shop.ItemForSale!.Position },
            new() { Type = AtkValueType.Int, Int = buyNow },
            new() { Type = 0, Int = 0 }
        };
        addonShop->FireCallback(4, buyItem);
    }

    public void SaveExternalPluginState()
    {
    }

    public unsafe void RestoreExternalPluginState()
    {
        if (_gameGuiAdapter.TryGetAddonByName("Shop", out AtkUnitBase* addonShop))
        {
            addonShop->FireCallbackInt(-1);
        }
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
                QuestStep? currentStep = FindCurrentStep();
                if (currentStep == null || currentStep.InteractionType != EInteractionType.PurchaseItem)
                {
                    return;
                }

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
                {
                    _shop.CancelAutoPurchase();
                }
            }
        }
    }

    private QuestStep? FindCurrentStep()
    {
        QuestController.QuestProgress? currentQuest = _questController.CurrentQuest;
        QuestSequence? currentSequence = currentQuest?.Quest.FindSequence(currentQuest.Sequence);
        return currentSequence?.FindStep(currentQuest?.Step ?? 0);
    }
}
