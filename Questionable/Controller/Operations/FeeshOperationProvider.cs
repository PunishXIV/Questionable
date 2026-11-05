using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;

namespace Questionable.Controller.Operations;

[RegisterSingleton<IOperationProvider, FeeshOperationProvider>(Duplicate = DuplicateStrategy.Append)]
internal sealed class FeeshOperationProvider(IFeeshIpc feeshIpc, FishingOperationState operation)
    : OwnedOperationProvider(operation)
{
    public override int Order => 400;
    public override string Name => "Feesh";
    protected override string CancelLabel => "Feesh fishing";
    public override bool HandlesLeadInTravel => true;

    protected override bool IsOwningExecutor(ITaskExecutor? executor) => executor is Fish.DoFish;
    protected override void PollOperation() => feeshIpc.PollOperation();
    protected override void CancelOwned() => feeshIpc.CancelOwned();
    protected override void ObserveIdle() => feeshIpc.ObserveIdle();

    public override bool MayBeMovingCharacter()
    {
        bool? busy = feeshIpc.GetBusy();
        return busy == true || (busy == null && feeshIpc.IsAvailable());
    }

    // Fish Feesh can't catch keep their travel for manual fishing.
    public override bool HandlesTravel(QuestStep step) => Fish.IsHandledByFeesh(step, feeshIpc);

    public override OperationStatusView? GetStatusView() => StatusView(
        _L("Check Feesh before restarting."),
        _L("Fishing for quest items with Feesh"),
        _L("I stopped Feesh"),
        _L("Finish or stop fishing in Feesh before confirming."),
        _L("WahTools must remain idle for at least 2 seconds. Confirmation only clears the block; start questing separately."));

    public override OperationPill? GetTitleBarPill() =>
        TitleBarPill(_L("Check Feesh"), _L("Fishing"), _L("Stopping fishing"));

    // WahTools drives the character while busy.
    public override string? GetStartBlockReason() =>
        feeshIpc.IsAvailable() && feeshIpc.GetBusy() != false
            ? _L("WahTools is busy or its status is unavailable. Finish its current work before starting questing.")
            : null;
}
