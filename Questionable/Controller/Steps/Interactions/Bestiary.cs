using ECommons.UIHelpers.AtkReaderImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Callback = ECommons.Automation.Callback;
namespace Questionable.Controller.Steps.Interactions;

// Bestiary callbacks follow AutoDuty's Crucible code (CrucibleUi).
internal static unsafe class Bestiary
{
    private const string Name = "XBMMonsterNotebook";
    private const uint OpenCommand = 100;

    public static bool TryGet(out AtkUnitBase* addon) =>
        GenericHelpers.TryGetAddonByName(Name, out addon) && GenericHelpers.IsAddonReady(addon);

    public static void Open() => UIModule.Instance()->ExecuteMainCommand(OpenCommand);

    public static void Close()
    {
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(Name, out var addon))
            Callback.Fire(addon, true, 0);
    }

    public static void NextPage(AtkUnitBase* addon, ReaderXBMMonsterNotebook reader) =>
        Callback.Fire(addon, true, 3, (reader.CurrentPage + 1) % reader.PageCount);
}
