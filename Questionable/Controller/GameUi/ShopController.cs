using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Questionable.Controller.GameUi.Shop;
using Questionable.Controller.GameUi.Shop.Model;
using Questionable.Model.Questing;
namespace Questionable.Controller.GameUi;

internal sealed class ShopController : IDisposable, IShopWindow
{
    private readonly IDataManager _dataManager;
    private readonly IFramework _framework;
    private readonly IGameGuiAdapter _gameGuiAdapter;
    private readonly ILogger<ShopController> _logger;
    private readonly QuestController _questController;
    private readonly RegularShopBase _shop;

    public ShopController(QuestController questController, IGameGui gameGui, IGameGuiAdapter gameGuiAdapter, IDataManager dataManager,
        IAddonLifecycle addonLifecycle, IFramework framework, ILogger<ShopController> logger, IPluginLog pluginLog)
    {
        _questController = questController;
        _gameGuiAdapter = gameGuiAdapter;
        _dataManager = dataManager;
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

        AddonMaster.Shop addonMaster = new(addon);
        AddonMaster.Shop.ShopItemInfo[] shopItems = addonMaster.ShopItems;
        if (shopItems.Length == 0)
        {
            _shop.ItemForSale = null;
            return;
        }

        _shop.ItemForSale = shopItems
            .Select((item, i) => new ItemForSale
            {
                Position = i,
                ItemId = item.ItemId,
                ItemName = _dataManager.GetExcelSheet<Item>().GetRowOrDefault(item.ItemId)?.Name.ToString() ?? string.Empty,
                Price = item.CostAmount,
                OwnedItems = (uint)_shop.GetItemCount(item.ItemId)
            })
            .FirstOrDefault(x => x.ItemId == currentStep.ItemId);
    }

    public unsafe void TriggerPurchase(AtkUnitBase* addonShop, int buyNow)
    {
        if (_shop.ItemForSale == null)
            return;

        AddonMaster.Shop addonMaster = new(addonShop);
        AddonMaster.Shop.ShopItemInfo? item = addonMaster.ShopItems.ElementAtOrDefault(_shop.ItemForSale.Position);
        item?.Select(buyNow);
    }

    public void SaveExternalPluginState()
    {
    }

    public unsafe void RestoreExternalPluginState()
    {
        if (_gameGuiAdapter.TryGetAddonByName("Shop", out AtkUnitBase* addonShop))
            addonShop->FireCallbackInt(-1);
    }

    private void FrameworkUpdate(IFramework framework)
    {
        if (IsOpen && _shop.ItemForSale != null)
        {
            if (_shop.PurchaseState != null)
                _shop.HandleNextPurchaseStep();
            else
            {
                QuestStep? currentStep = FindCurrentStep();
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

    private QuestStep? FindCurrentStep()
    {
        QuestController.QuestProgress? currentQuest = _questController.CurrentQuest;
        QuestSequence? currentSequence = currentQuest?.Quest.FindSequence(currentQuest.Sequence);
        return currentSequence?.FindStep(currentQuest?.Step ?? 0);
    }
}
