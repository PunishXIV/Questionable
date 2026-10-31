// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

/// <summary>
///     Knightshopper's purchase IPC, see <c>docs/IPC.md</c> in its install folder.
/// </summary>
[RegisterSingleton]
internal sealed class KnightshopperIpc(IDalamudPluginInterface pluginInterface, ILogger<KnightshopperIpc> logger) : Ipc
{
    /// <summary>
    ///     First API version that has <c>Knightshopper.Purchase.StartItems</c>.
    /// </summary>
    private const int MinimumApiVersion = 2;

    private const int GilCurrency = 2;

    public enum EStartResult
    {
        Started = 0,
        Busy = 1,
        InvalidCurrency = 2,
        EmptyList = 3,
        NotReady = 4,
        NotLoggedIn = 5,
        NotAllowed = 6,
        ItemUnavailable = 7,
        InvalidQuantity = 8,
        ListNotFound = 9
    }

    public enum EPurchaseState
    {
        Unknown = 0,
        Running = 1,
        CancellationRequested = 2,
        Succeeded = 3,
        Failed = 4,
        Cancelled = 5
    }

    public override string InternalName => "Knightshopper";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() => _apiVersion.InvokeFunc() >= MinimumApiVersion, fallback: false);

    private readonly ICallGateSubscriber<int> _apiVersion = pluginInterface.GetIpcSubscriber<int>("Knightshopper.ApiVersion");

    private readonly ICallGateSubscriber<int, (uint ItemId, uint Quantity)[], (int, Guid, string)> _startItems =
        pluginInterface.GetIpcSubscriber<int, (uint ItemId, uint Quantity)[], (int, Guid, string)>("Knightshopper.Purchase.StartItems");

    private readonly ICallGateSubscriber<Guid, (int, int, int, uint, string)> _getStatus =
        pluginInterface.GetIpcSubscriber<Guid, (int, int, int, uint, string)>("Knightshopper.Purchase.GetStatus");

    private readonly ICallGateSubscriber<Guid, bool> _cancel = pluginInterface.GetIpcSubscriber<Guid, bool>("Knightshopper.Purchase.Cancel");

    /// <summary>
    ///     Starts buying an item with gil. <paramref name="quantity" /> is the total to own afterwards, not the amount
    ///     to buy.
    /// </summary>
    public EStartResult BuyWithGil(uint itemId, uint quantity, out Guid operationId, out string message)
    {
        try
        {
            logger.LogInformation("Asking Knightshopper to buy {ItemId} up to {Quantity}", itemId, quantity);
            (int result, operationId, message) = _startItems.InvokeFunc(GilCurrency, [(itemId, quantity)]);
            return (EStartResult)result;
        }
        catch (IpcError e)
        {
            logger.LogError(e, "Unable to start Knightshopper purchase");
            operationId = Guid.Empty;
            message = e.Message;
            return EStartResult.NotReady;
        }
    }

    public (EPurchaseState State, string Message) GetStatus(Guid operationId)
    {
        try
        {
            (int state, int _, int _, uint _, string message) = _getStatus.InvokeFunc(operationId);
            return ((EPurchaseState)state, message);
        }
        catch (IpcError e)
        {
            logger.LogWarning(e, "Unable to read Knightshopper purchase status");
            return (EPurchaseState.Unknown, e.Message);
        }
    }

    public void Cancel(Guid operationId)
    {
        try
        {
            _cancel.InvokeFunc(operationId);
        }
        catch (IpcError e)
        {
            logger.LogWarning(e, "Unable to cancel Knightshopper purchase");
        }
    }
}
