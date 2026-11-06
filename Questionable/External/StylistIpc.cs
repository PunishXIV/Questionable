using Dalamud.Plugin.Ipc;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class StylistIpc(IDalamudPluginInterface pluginInterface, ILogger<StylistIpc> logger) : Ipc
{
    private const string PluginName = "Stylist";

    public override string InternalName => PluginName;
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() => GetVersion() != null && !_isBusy.InvokeFunc(), fallback: false);
    private readonly ICallGateSubscriber<bool> _isBusy = pluginInterface.GetIpcSubscriber<bool>("Stylist.IsBusy");
    private readonly ICallGateSubscriber<bool?, bool?, object?> _updateGearset = pluginInterface.GetIpcSubscriber<bool?, bool?, object?>("Stylist.UpdateCurrentGearsetEx"); //bool? moveItemsFromInventory, bool? shouldEquip
    private bool _loggedIpcError;

    public static bool IsInstalled => IPCSubscriber.IsInstalled(PluginName);

    public bool WarnedMissing { get; set; }

    public bool? IsBusy => IsInstalled ? IpcInvoke.SafeFunc<bool?>(() => _isBusy.InvokeFunc(), fallback: null) : null;

    public bool UpdateGearset()
    {
        try
        {
            _updateGearset.InvokeAction(true, true);
            return true;
        }
        catch (Exception e) when (IpcInvoke.IsSafeIpcFailure(e))
        {
            if (!_loggedIpcError)
            {
                _loggedIpcError = true;
                logger.LogWarning(e, "Could not query stylist to update gearset, probably not installed");
            }

            return false;
        }
    }
}
