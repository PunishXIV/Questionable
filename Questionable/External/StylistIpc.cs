using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Microsoft.Extensions.Logging;
namespace Questionable.External;

internal sealed class StylistIpc
{
    private readonly ICallGateSubscriber<bool> _isBusy;
    private readonly ILogger<AutomatonIpc> _logger;
    private readonly ICallGateSubscriber<bool?, bool?, object?> _updateGearset; //bool? moveItemsFromInventory, bool? shouldEquip
    private bool _loggedIpcError;

    public StylistIpc(IDalamudPluginInterface pluginInterface, ILogger<AutomatonIpc> logger)
    {
        _logger = logger;
        _updateGearset = pluginInterface.GetIpcSubscriber<bool?, bool?, object?>("Stylist.UpdateCurrentGearsetEx");
        _isBusy = pluginInterface.GetIpcSubscriber<bool>("Stylist.IsBusy");
    }

    public bool IsBusy =>
        _isBusy.InvokeFunc();

    public void UpdateGearset()
    {
        try
        {
            _updateGearset.InvokeAction(true, true);
        }
        catch(IpcError e)
        {
            if (!_loggedIpcError)
            {
                _loggedIpcError = true;
                _logger.LogWarning(e, "Could not query stylist to update gearset, probably not installed");
            }
        }
    }
}
