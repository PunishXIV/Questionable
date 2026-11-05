using Questionable.Controller.Steps.Interactions;

namespace Questionable.Controller.Operations;

[RegisterSingleton<IOperationProvider, AutoDutyOperationProvider>(Duplicate = DuplicateStrategy.Append)]
internal sealed class AutoDutyOperationProvider(AutoDutyIpc autoDutyIpc) : OwnedOperationProvider(autoDutyIpc.Operation)
{
    public override int Order => 500;
    public override string Name => "AutoDuty";
    protected override string CancelLabel => "AutoDuty";

    protected override bool IsOwningExecutor(ITaskExecutor? executor) => executor is Duty.StartAutoDutyExecutor;
    protected override void PollOperation() => autoDutyIpc.PollOperation();
    protected override void CancelOwned() => autoDutyIpc.CancelOwned();
    protected override void ObserveIdle() => autoDutyIpc.ObserveIdle();

    // AutoDuty owns its death and retry behaviour while it runs our duty.
    public override bool HandlesPlayerDeath(ITaskExecutor? executor) => OwnsExecutor(executor);

    public override bool MayBeMovingCharacter() => autoDutyIpc.IsAvailable() && autoDutyIpc.GetStopped() != true;

    public override OperationStatusView? GetStatusView()
    {
        if (!autoDutyIpc.HasOperation)
            return null;

        return new(autoDutyIpc.Operation.BlockReason ?? _L("Running a duty with AutoDuty"),
            autoDutyIpc.RequiresReconciliation
                ? new OperationRecoveryView(_L("I stopped AutoDuty"), autoDutyIpc.CanConfirmIdle,
                    [_L("Stop AutoDuty and leave its duty or queue before confirming. Movement and temporary settings must be settled. Confirmation does not restart questing.")],
                    Detail: autoDutyIpc.Operation.Message)
                : null);
    }

    public override OperationPill? GetTitleBarPill() => null;

    public override string? GetStartBlockReason() =>
        MayBeMovingCharacter()
            ? _L("AutoDuty is busy or its status is unavailable. Finish its current work before starting questing.")
            : null;
}
