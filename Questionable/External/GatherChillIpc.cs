// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class GatherChillIpc(IDalamudPluginInterface pluginInterface, ILogger<GatherChillIpc> logger) : Ipc
{
    /// <summary>
    /// First API version that has <c>GatherChill.GatherItem</c>.
    /// </summary>
    private const int MinimumApiVersion = 2;

    public override string InternalName => "GatherChill";
    public override Version? GetVersion() => IPCSubscriber.Version("Gather & Chill");
    public override bool IsReady() => IpcInvoke.SafeFunc(() => _apiVersion.InvokeFunc() >= MinimumApiVersion, fallback: false);

    private readonly ICallGateSubscriber<int> _apiVersion = pluginInterface.GetIpcSubscriber<int>("GatherChill.ApiVersion");
    private readonly ICallGateSubscriber<bool> _isBusy = pluginInterface.GetIpcSubscriber<bool>("GatherChill.IsBusy");
    private readonly ICallGateSubscriber<uint, bool> _canGather = pluginInterface.GetIpcSubscriber<uint, bool>("GatherChill.CanGather");
    private readonly ICallGateSubscriber<uint, int, bool> _gatherItem = pluginInterface.GetIpcSubscriber<uint, int, bool>("GatherChill.GatherItem");
    private readonly ICallGateSubscriber<object> _stop = pluginInterface.GetIpcSubscriber<object>("GatherChill.Stop");

    public bool IsBusy() => IpcInvoke.SafeFunc(() => _isBusy.InvokeFunc(), fallback: false);

    public bool CanGather(uint itemId) => IpcInvoke.SafeFunc(() => _canGather.InvokeFunc(itemId), fallback: false);

    public bool GatherItem(uint itemId, int quantity)
    {
        try
        {
            logger.LogInformation("Attempting to gather {Quantity}x {ItemId} with Gather n Chill", quantity, itemId);
            return _gatherItem.InvokeFunc(itemId, quantity);
        }
        catch (IpcError e)
        {
            logger.LogError(e, "Unable to gather items");
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            _stop.InvokeAction();
        }
        catch (IpcError e)
        {
            logger.LogWarning(e, "Unable to stop Gather n Chill");
        }
    }
}
