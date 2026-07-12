using FFXIVClientStructs.FFXIV.Component.GUI;
namespace Questionable.Utils;

internal unsafe interface IGameGuiAdapter
{
    bool TryGetAddonByName(string name, out AtkUnitBase* addon);
    bool TryGetAddonByName<TAddon>(string name, out TAddon* addon) where TAddon : unmanaged;
}
