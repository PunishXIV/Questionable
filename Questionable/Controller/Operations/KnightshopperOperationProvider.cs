using Questionable.Controller.Steps.Shared;
using Questionable.Data;
using Questionable.Model.Questing;

namespace Questionable.Controller.Operations;

[RegisterSingleton<IOperationProvider, KnightshopperOperationProvider>(Duplicate = DuplicateStrategy.Append)]
internal sealed class KnightshopperOperationProvider(
    IKnightshopperIpc knightshopperIpc,
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
