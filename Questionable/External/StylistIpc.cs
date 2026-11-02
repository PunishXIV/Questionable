using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class StylistIpc(IDalamudPluginInterface pluginInterface, ILogger<StylistIpc> logger) : Ipc
{
    public override string InternalName => "Stylist";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() => GetVersion() != null && !_isBusy.InvokeFunc(), fallback: false);
    private readonly ICallGateSubscriber<bool> _isBusy = pluginInterface.GetIpcSubscriber<bool>("Stylist.IsBusy");
    private readonly ICallGateSubscriber<bool?, bool?, object?> _updateGearset = pluginInterface.GetIpcSubscriber<bool?, bool?, object?>("Stylist.UpdateCurrentGearsetEx"); //bool? moveItemsFromInventory, bool? shouldEquip
    private bool _loggedIpcError;

    public static bool IsInstalled => IPCSubscriber.IsInstalled("Stylist");

    /// <summary>Set once the missing-Stylist fallback has been announced, so it is printed once per session.</summary>
    public bool WarnedMissing { get; set; }

    /// <returns>Whether Stylist is busy, or null when it is not installed or its IPC is unavailable.</returns>
    public bool? IsBusy => IsInstalled ? IpcInvoke.SafeFunc<bool?>(() => _isBusy.InvokeFunc(), fallback: null) : null;

    /// <returns>Whether the update request reached Stylist.</returns>
    public bool UpdateGearset()
    {
        try
        {
            _updateGearset.InvokeAction(true, true);
            return true;
        }
        catch (IpcError e)
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
