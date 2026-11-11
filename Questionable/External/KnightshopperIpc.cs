using Questionable.Controller.Steps.Shared;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class KnightshopperIpc : Ipc, IDisposable
{
    private const int MinimumApiVersion = 2;


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
    private readonly PurchaseState _operation;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("ApiVersion")] private readonly Func<int> _apiVersion = null!;
    [EzIPC("IsBusy")] private readonly Func<bool> _isBusy = null!;
    [EzIPC("Purchase.StartItems")] private readonly Func<int, (uint ItemId, uint Quantity)[], (int, Guid, string)> _startItems = null!;
    [EzIPC("Purchase.GetStatus")] private readonly Func<Guid, (int, int, int, uint, string)> _getStatus = null!;
    [EzIPC("Purchase.Cancel")] private readonly Func<Guid, bool> _cancel = null!;

    public KnightshopperIpc(ILogger<KnightshopperIpc> logger, PurchaseState operation)
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
    public EStartResult Buy(int currency, uint itemId, uint quantity, out Guid operationId, out string message)
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
        Guid id = _operation.BeginRequest();
        try
        {
            _logger.LogInformation("Asking Knightshopper to buy {ItemId} up to {Quantity} with currency {Currency}",
                itemId, quantity, currency);
            (int result, operationId, message) = _startItems(currency, [(itemId, quantity)]);
            if (result == (int)EStartResult.Started)
            {
                _operation.AcceptRequest(id, operationId);
                return operationId != Guid.Empty ? EStartResult.Started : EStartResult.Uncertain;
            }

            if (Enum.IsDefined((EStartResult)result) && result >= (int)EStartResult.Busy)
            {
                _operation.RejectRequest(id);
                return (EStartResult)result;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to start Knightshopper purchase");
            message = e.Message;
        }

        _operation.MarkUncertain(id);
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
        if (_operation.Id is not { } id)
            return;
        if (_operation.ProviderId is { } providerId)
        {
            var (state, message) = GetStatus(providerId);
            _operation.ObservePurchase(id, state, message);
        }
        if (_operation.IsAbandoned)
            CancelWork();
        if (_operation.NeedsConfirmation)
            ObserveIdle();
    }

    public void CancelWork()
    {
        _operation.Abandon();
        if (!_operation.TryBeginCancel(out Guid providerId))
            return;
        try
        {
            _logger.LogInformation("Requesting cancellation of Knightshopper purchase {Id}", providerId);
            _operation.ObserveCancel(providerId, _cancel(providerId));
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to cancel Knightshopper purchase {Id}", providerId);
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

[RegisterSingleton<IOperationProvider, KnightshopperProvider>(Duplicate = DuplicateStrategy.Append)]
internal sealed class KnightshopperProvider(
    KnightshopperIpc knightshopperIpc,
    PurchaseState operation,
    GilShopData gilShopData,
    TomestoneShopData tomestoneShopData) : PluginOperationProvider(operation)
{
    public override int Order => 200;
    public override string Name => "Knightshopper";
    protected override string CancelLabel => "Knightshopper purchase";

    protected override bool MatchesExecutor(ITaskExecutor? executor) => executor is Purchase.BuyExecutor;
    protected override void PollOperation() => knightshopperIpc.PollOperation();
    protected override void CancelWork() => knightshopperIpc.CancelWork();
    protected override void ObserveIdle() => knightshopperIpc.ObserveIdle();

    public override bool MayBeMovingCharacter()
    {
        bool? busy = knightshopperIpc.GetBusy();
        return busy == true || (busy == null && knightshopperIpc.IsReady());
    }

    // Walk steps before a purchase keep their travel; only the purchase step's own travel is skipped.
    public override bool HandlesTravel(QuestStep step) =>
        Purchase.IsHandledByKnightshopper(step, gilShopData, tomestoneShopData);

    public override OperationStatusView? GetStatusView() => StatusView(
        _L("Check Knightshopper before restarting."),
        _L("Buying quest items with Knightshopper"),
        _L("I stopped Knightshopper"),
        _L("Finish or stop the purchase in Knightshopper before confirming."),
        _L("Knightshopper must remain idle for at least 2 seconds. Confirmation only clears the block; start questing separately."));

    public override OperationPill? GetTitleBarPill() =>
        TitleBarPill(_L("Check Knightshopper"), _L("Purchasing"), _L("Stopping purchase"));

    // Manual Knightshopper work has no id here, so block on busy or unreadable status.
    public override string? GetStartBlockReason() =>
        knightshopperIpc.IsReady() && knightshopperIpc.GetBusy() != false
            ? _L("Knightshopper is busy or its status is unavailable. Finish its current work before starting questing.")
            : null;
}

[RegisterSingleton]
internal sealed class PurchaseState(TimeProvider? timeProvider = null)
    : PluginOperationState("Knightshopper", "buying quest items", timeProvider)
{
    public void ObservePurchase(Guid id, ExternalOperationStatus status, string message) =>
        ObserveStatus(id, status, message);
}
