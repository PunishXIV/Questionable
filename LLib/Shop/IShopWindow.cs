using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LLib.Shop;

public interface IShopWindow
{
    public bool IsEnabled { get; }
    public bool IsOpen { get; set; }
    public Vector2? Position { get; set; }

    public int GetCurrencyCount();
    public unsafe void UpdateShopStock(AtkUnitBase* addon);
    public unsafe void TriggerPurchase(AtkUnitBase* addonShop, int buyNow);
    public void SaveExternalPluginState();
    public void RestoreExternalPluginState();
}
