using System;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LLib.GameUI;

public static class LAddon
{
    private const int UnitListCount = 18;

    public static unsafe AtkUnitBase* GetAddonById(uint id)
    {
        var unitManagers = &AtkStage.Instance()->RaptureAtkUnitManager->AtkUnitManager.DepthLayerOneList;
        for (var i = 0; i < UnitListCount; i++)
        {
            var unitManager = &unitManagers[i];
            foreach (var j in Enumerable.Range(0, Math.Min(unitManager->Count, unitManager->Entries.Length)))
            {
                var unitBase = unitManager->Entries[j].Value;
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

        var a = gameGui.GetAddonByName(addonName);
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
