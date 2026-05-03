using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.GameUI;
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
        return gameGui.TryGetAddonByName(name, out addon);
    }

    public bool TryGetAddonByName<TAddon>(string name, out TAddon* addon) where TAddon : unmanaged
    {
        return gameGui.TryGetAddonByName(name, out addon);
    }
}
