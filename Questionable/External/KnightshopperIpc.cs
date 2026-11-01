using Dalamud.Plugin.Ipc;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

/// <summary>
///     Knightshopper's purchase IPC, see <c>docs/IPC.md</c> in its install folder.
/// </summary>
[RegisterSingleton<IKnightshopperIpc, KnightshopperIpc>]
internal sealed class KnightshopperIpc(IDalamudPluginInterface pluginInterface, ILogger<KnightshopperIpc> logger,
    PurchaseOperationState operation) : Ipc, IKnightshopperIpc
{
    /// <summary>
    ///     First API version that has <c>Knightshopper.Purchase.StartItems</c>.
    /// </summary>
    private const int MinimumApiVersion = 2;

    private const int GilCurrency = 2;

    public enum EStartResult
    {
        Uncertain = -1,
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
    public override bool IsReady()
    {
        try
        {
            return _apiVersion.InvokeFunc() >= MinimumApiVersion;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private readonly ICallGateSubscriber<int> _apiVersion = pluginInterface.GetIpcSubscriber<int>("Knightshopper.ApiVersion");
    private readonly ICallGateSubscriber<bool> _isBusy = pluginInterface.GetIpcSubscriber<bool>("Knightshopper.IsBusy");

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
        operationId = Guid.Empty;
        message = "";
        if (operation.HasOperation)
            return EStartResult.Busy;
        if (itemId == 0 || quantity == 0)
            return EStartResult.InvalidQuantity;
        if (!IsReady())
            return EStartResult.NotReady;
        bool? busy = GetBusy();
        if (busy != false)
            return busy == true ? EStartResult.Busy : EStartResult.NotReady;

        // Record ownership before invoking a provider: an exception can occur after acceptance.
        Guid owner = operation.BeginSubmission();
        try
        {
            logger.LogInformation("Asking Knightshopper to buy {ItemId} up to {Quantity}", itemId, quantity);
            (int result, operationId, message) = _startItems.InvokeFunc(GilCurrency, [(itemId, quantity)]);
            if (result == (int)EStartResult.Started)
            {
                operation.AcceptSubmission(owner, operationId);
                return operationId != Guid.Empty ? EStartResult.Started : EStartResult.Uncertain;
            }

            if (Enum.IsDefined((EStartResult)result) && result >= (int)EStartResult.Busy)
            {
                operation.RejectSubmission(owner);
                return (EStartResult)result;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to start Knightshopper purchase");
            message = e.Message;
        }

        operation.MarkUncertain(owner);
        return EStartResult.Uncertain;
    }

    private (EPurchaseState State, string Message) GetStatus(Guid operationId)
    {
        try
        {
            (int state, int _, int _, uint _, string message) = _getStatus.InvokeFunc(operationId);
            return ((EPurchaseState)state, message);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Unable to read Knightshopper purchase status");
            return (EPurchaseState.Unknown, e.Message);
        }
    }

    public void PollOperation()
    {
        if (operation.OperationId is not { } owner)
            return;
        if (operation.ProviderOperationId is { } providerId)
        {
            var (state, message) = GetStatus(providerId);
            operation.ObserveStatus(owner, state, message);
        }
        if (operation.IsAbandoned)
            CancelOwned();
        if (operation.RequiresReconciliation)
            ObserveIdle();
    }

    public void CancelOwned()
    {
        operation.AbandonCurrent();
        if (!operation.TryBeginCancellation(out Guid providerId))
            return;
        try
        {
            logger.LogInformation("Requesting cancellation of Knightshopper purchase {OperationId}", providerId);
            operation.ObserveCancellation(providerId, _cancel.InvokeFunc(providerId));
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Unable to cancel Knightshopper purchase {OperationId}", providerId);
        }
    }

    public void ObserveIdle()
        => operation.ObserveIdle(IsReady() ? GetBusy() : null);

    public bool? GetBusy()
    {
        try
        {
            return _isBusy.InvokeFunc();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
