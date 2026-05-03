using Dalamud.Game.NativeWrapper;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Linq;
namespace LLib.GameUI;

public static class LLAddon
{
    private const int UnitListCount = 18;

    public static unsafe AtkUnitBase* GetAddonById(uint id)
    {
        AtkUnitList* unitManagers = &AtkStage.Instance()->RaptureAtkUnitManager->AtkUnitManager.DepthLayerOneList;
        for(int i = 0; i < UnitListCount; i++)
        {
            AtkUnitList* unitManager = &unitManagers[i];
            foreach(int j in Enumerable.Range(0, Math.Min(unitManager->Count, unitManager->Entries.Length)))
            {
                AtkUnitBase* unitBase = unitManager->Entries[j].Value;
                if (unitBase != null && unitBase->Id == id)
                {
                    return unitBase;
                }
            }
        }

        return null;
    }

    public static unsafe bool TryGetAddonByName<T>(this IGameGui gameGui, string addonName, out T* addonPtr)
    where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(gameGui);
        ArgumentException.ThrowIfNullOrEmpty(addonName);

        AtkUnitBasePtr a = gameGui.GetAddonByName(addonName);
        if (!a.IsNull)
        {
            addonPtr = (T*)a.Address;
            return true;
        }
        else
        {
            addonPtr = null;
            return false;
        }
    }

    public static unsafe bool IsAddonReady(AtkUnitBase* addon)
    {
        return addon->IsVisible && addon->UldManager.LoadedState == AtkLoadState.Loaded;
    }
}
