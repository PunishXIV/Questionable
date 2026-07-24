using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

internal sealed class StylistIpc(IDalamudPluginInterface pluginInterface, ILogger<StylistIpc> logger)
{
    private readonly ICallGateSubscriber<bool> _isBusy = pluginInterface.GetIpcSubscriber<bool>("Stylist.IsBusy");
    private readonly ICallGateSubscriber<bool?, bool?, object?> _updateGearset = pluginInterface.GetIpcSubscriber<bool?, bool?, object?>("Stylist.UpdateCurrentGearsetEx"); //bool? moveItemsFromInventory, bool? shouldEquip
    private bool _loggedIpcError;

    public static bool IsInstalled => IPCSubscriber.IsInstalled("Stylist");

    public bool IsBusy => !IsInstalled || _isBusy.InvokeFunc();

    public void UpdateGearset()
    {
        try
        {
            _updateGearset.InvokeAction(true, true);
        }
        catch (IpcError e)
        {
            if (!_loggedIpcError)
            {
                _loggedIpcError = true;
                logger.LogWarning(e, "Could not query stylist to update gearset, probably not installed");
            }
        }
    }
}
