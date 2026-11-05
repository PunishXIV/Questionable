using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;

namespace Questionable.Controller.Operations;

[RegisterSingleton<IOperationProvider, GatherChillOperationProvider>(Duplicate = DuplicateStrategy.Append)]
internal sealed class GatherChillOperationProvider(IGatherChillIpc gatherChillIpc, GatheringOperationState operation)
    : OwnedOperationProvider(operation)
{
    public override int Order => 300;
    public override string Name => "Gather n Chill";
    protected override string CancelLabel => "gathering";
    public override bool HandlesLeadInTravel => true;

    protected override bool IsOwningExecutor(ITaskExecutor? executor) => executor is Gather.StartGathering;
    protected override void PollOperation() => gatherChillIpc.PollOperation();
    protected override void CancelOwned() => gatherChillIpc.CancelOwned();
    protected override void ObserveIdle() => gatherChillIpc.ObserveIdle();

    public override bool MayBeMovingCharacter()
    {
        bool? busy = gatherChillIpc.GetBusy();
        return busy == true || (busy == null && gatherChillIpc.IsAvailable());
    }

    // Gathering always goes through Gather n Chill; steps naming a quest-specific node (e.g. 3833) still walk there.
    public override bool HandlesTravel(QuestStep step) => step is { InteractionType: EInteractionType.Gather, DataId: null };

    public override OperationStatusView? GetStatusView() => StatusView(
        _L("Check Gather n Chill before restarting."),
        _L("Gathering quest items with Gather n Chill"),
        _L("I stopped Gather n Chill"),
        _L("Finish or stop gathering in Gather n Chill before confirming."),
        _L("Gather n Chill must remain idle for at least 2 seconds. Confirmation only clears the block; start questing separately."));

    public override OperationPill? GetTitleBarPill() =>
        TitleBarPill(_L("Check Gather n Chill"), _L("Gathering"), _L("Stopping gathering"));

    public override string? GetStartBlockReason() =>
        gatherChillIpc.IsAvailable() && gatherChillIpc.GetBusy() != false
            ? _L("Gather n Chill is busy or its status is unavailable. Finish its current work before starting questing.")
            : null;
}
