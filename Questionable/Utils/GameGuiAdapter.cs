using Dalamud.Game.NativeWrapper;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace Questionable.Utils;

[RegisterSingleton<IGameGuiAdapter, GameGuiAdapter>]
internal sealed unsafe class GameGuiAdapter(IGameGui gameGui) : IGameGuiAdapter
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
