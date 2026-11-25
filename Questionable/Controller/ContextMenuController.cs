using Dalamud.Game.Gui.ContextMenu;
using ECommons.ExcelServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class ContextMenuController : IDisposable
{
    private readonly IContextMenu _contextMenu;

    private IEnumerable<ulong> _disabledCharacterIds = [];

    public ContextMenuController(IContextMenu contextMenu)
    {
        _contextMenu = contextMenu;
        _contextMenu.OnMenuOpened += MenuOpened;
    }

    public void Dispose() => _contextMenu.OnMenuOpened -= MenuOpened;

    private void MenuOpened(IMenuOpenedArgs args)
    {
        // The game reports this as a null addon name.
        if (args.AddonName != null)
            return;

        if (TryGetHoveredCharacter(args, out var hovered) && hovered is AddonMaster._CharaSelectListMenu.Character character)
            args.AddMenuItem(CreateCharacterItem(character));
    }

    private static bool TryGetHoveredCharacter(
        IMenuOpenedArgs args,
        out AddonMaster._CharaSelectListMenu.Character? character)
    {
        character = null;
        if (GenericHelpers.TryGetAddonMaster<AddonMaster._CharaSelectListMenu>(out var menu))
            if (args.Target is MenuTargetDefault target && menu.Characters.TryGetFirst(x => x.IsSelected, out var selected))
                character = selected;
        return character != null;
    }

    private void DisableCharacterId(ulong characterId)
    {
        _disabledCharacterIds = _disabledCharacterIds.Append(characterId);
    }

    private unsafe MenuItem CreateCharacterItem(
        AddonMaster._CharaSelectListMenu.Character character)
    {
        bool characterDisabled = _disabledCharacterIds.Contains(character.Entry->ContentId);
        string characterId = $"FFXIV_CHR{character.Entry->ContentId:X16}";
        return new()
        {
            Prefix = SeIconChar.Hexagon,
            PrefixColor = 52,
            Name = (characterDisabled ? "ID not found" : "Open Data") + $" (*{characterId[^5..]})",
            IsEnabled = true,
            OnClicked = _1 =>
            {
                if (character != null)
                {
                    var path = Path.Combine(Framework.Instance()->UserPathString, characterId);
                    if (!Path.Exists(path))
                    {
                        DisableCharacterId(character.Entry->ContentId);
                        return;
                    }
                    string homeWorld = ExcelWorldHelper.GetName(character.HomeWorld);
                    try
                    {
                        File.Create(Path.Combine(path, $"_{character.Name}@{homeWorld}")).Dispose();
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
        };
    }
}
