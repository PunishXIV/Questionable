using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class KnightshopperIpc : Ipc, IDisposable
{
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

    private readonly ILogger<KnightshopperIpc> _logger;
    private readonly PurchaseOperationState _operation;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("ApiVersion")] private readonly Func<int> _apiVersion = null!;
    [EzIPC("IsBusy")] private readonly Func<bool> _isBusy = null!;
    [EzIPC("Purchase.StartItems")] private readonly Func<int, (uint ItemId, uint Quantity)[], (int, Guid, string)> _startItems = null!;
    [EzIPC("Purchase.GetStatus")] private readonly Func<Guid, (int, int, int, uint, string)> _getStatus = null!;
    [EzIPC("Purchase.Cancel")] private readonly Func<Guid, bool> _cancel = null!;

    public KnightshopperIpc(ILogger<KnightshopperIpc> logger, PurchaseOperationState operation)
    {
        _logger = logger;
        _operation = operation;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
    }

    public override string InternalName => "Knightshopper";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady()
    {
        try
        {
            return _apiVersion() >= MinimumApiVersion;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // quantity is the total to own afterwards, not the amount to buy.
    public EStartResult BuyWithGil(uint itemId, uint quantity, out Guid operationId, out string message)
    {
        operationId = Guid.Empty;
        message = "";
        if (_operation.HasOperation)
            return EStartResult.Busy;
        if (itemId == 0 || quantity == 0)
            return EStartResult.InvalidQuantity;
        if (!IsReady())
            return EStartResult.NotReady;
        bool? busy = GetBusy();
        if (busy != false)
            return busy == true ? EStartResult.Busy : EStartResult.NotReady;

        // Record ownership before invoking a provider: an exception can occur after acceptance.
        Guid owner = _operation.BeginSubmission();
        try
        {
            _logger.LogInformation("Asking Knightshopper to buy {ItemId} up to {Quantity}", itemId, quantity);
            (int result, operationId, message) = _startItems(GilCurrency, [(itemId, quantity)]);
            if (result == (int)EStartResult.Started)
            {
                _operation.AcceptSubmission(owner, operationId);
                return operationId != Guid.Empty ? EStartResult.Started : EStartResult.Uncertain;
            }

            if (Enum.IsDefined((EStartResult)result) && result >= (int)EStartResult.Busy)
            {
                _operation.RejectSubmission(owner);
                return (EStartResult)result;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to start Knightshopper purchase");
            message = e.Message;
        }

        _operation.MarkUncertain(owner);
        return EStartResult.Uncertain;
    }

    private (ExternalOperationStatus State, string Message) GetStatus(Guid operationId)
    {
        try
        {
            (int state, int _, int _, uint _, string message) = _getStatus(operationId);
            return ((ExternalOperationStatus)state, message);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to read Knightshopper purchase status");
            return (ExternalOperationStatus.Unknown, e.Message);
        }
    }

    public void PollOperation()
    {
        if (_operation.OperationId is not { } owner)
            return;
        if (_operation.ProviderOperationId is { } providerId)
        {
            var (state, message) = GetStatus(providerId);
            _operation.ObserveStatus(owner, state, message);
        }
        if (_operation.IsAbandoned)
            CancelOwned();
        if (_operation.RequiresReconciliation)
            ObserveIdle();
    }

    public void CancelOwned()
    {
        _operation.AbandonCurrent();
        if (!_operation.TryBeginCancellation(out Guid providerId))
            return;
        try
        {
            _logger.LogInformation("Requesting cancellation of Knightshopper purchase {OperationId}", providerId);
            _operation.ObserveCancellation(providerId, _cancel(providerId));
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to cancel Knightshopper purchase {OperationId}", providerId);
        }
    }

    public void ObserveIdle()
        => _operation.ObserveIdle(IsReady() ? GetBusy() : null);

    public bool? GetBusy()
    {
        try
        {
            return _isBusy();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}

[RegisterSingleton<IOperationProvider, KnightshopperOperationProvider>(Duplicate = DuplicateStrategy.Append)]
internal sealed class KnightshopperOperationProvider(
    KnightshopperIpc knightshopperIpc,
    PurchaseOperationState operation,
    GilShopData gilShopData) : OwnedOperationProvider(operation)
{
    public override int Order => 200;
    public override string Name => "Knightshopper";
    protected override string CancelLabel => "Knightshopper purchase";

    protected override bool IsOwningExecutor(ITaskExecutor? executor) => executor is Purchase.DoBuy;
    protected override void PollOperation() => knightshopperIpc.PollOperation();
    protected override void CancelOwned() => knightshopperIpc.CancelOwned();
    protected override void ObserveIdle() => knightshopperIpc.ObserveIdle();

    public override bool MayBeMovingCharacter()
    {
        bool? busy = knightshopperIpc.GetBusy();
        return busy == true || (busy == null && knightshopperIpc.IsReady());
    }

    // Walk steps before a purchase keep their travel; only the purchase step's own travel is skipped.
    public override bool HandlesTravel(QuestStep step) => Purchase.IsHandledByKnightshopper(step, gilShopData);

    public override OperationStatusView? GetStatusView() => StatusView(
        _L("Check Knightshopper before restarting."),
        _L("Buying quest items with Knightshopper"),
        _L("I stopped Knightshopper"),
        _L("Finish or stop the purchase in Knightshopper before confirming."),
        _L("Knightshopper must remain idle for at least 2 seconds. Confirmation only clears the block; start questing separately."));

    public override OperationPill? GetTitleBarPill() =>
        TitleBarPill(_L("Check Knightshopper"), _L("Purchasing"), _L("Stopping purchase"));

    // Manual Knightshopper work has no owner here, so block on busy or unreadable status.
    public override string? GetStartBlockReason() =>
        knightshopperIpc.IsReady() && knightshopperIpc.GetBusy() != false
            ? _L("Knightshopper is busy or its status is unavailable. Finish its current work before starting questing.")
            : null;
}

[RegisterSingleton]
internal sealed class PurchaseOperationState(TimeProvider? timeProvider = null)
    : OwnedOperationState("Knightshopper", "buying quest items", timeProvider)
{
    public void ObserveStatus(Guid owner, ExternalOperationStatus status, string message) =>
        ObserveOperationStatus(owner, status, message);
}
