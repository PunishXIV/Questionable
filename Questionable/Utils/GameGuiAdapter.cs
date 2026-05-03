using Dalamud.Game.NativeWrapper;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace Questionable.Utils;

internal unsafe interface IGameGuiAdapter
{
    bool TryGetAddonByName(string name, out AtkUnitBase* addon);
    bool TryGetAddonByName<TAddon>(string name, out TAddon* addon) where TAddon : unmanaged;
}

internal sealed unsafe class LLibGameGuiAdapter(IGameGui gameGui) : IGameGuiAdapter
{
    public bool TryGetAddonByName(string name, out AtkUnitBase* addon)
    {
        AtkUnitBasePtr a = gameGui.GetAddonByName(name);
        if (!a.IsNull)
        {
            addon = (AtkUnitBase*)a.Address;
            return true;
        }

        addon = null;
        return false;
    }

    public bool TryGetAddonByName<TAddon>(string name, out TAddon* addon) where TAddon : unmanaged
    {
        AtkUnitBasePtr a = gameGui.GetAddonByName(name);
        if (!a.IsNull)
        {
            addon = (TAddon*)a.Address;
            return true;
        }

        addon = null;
        return false;
    }
}
