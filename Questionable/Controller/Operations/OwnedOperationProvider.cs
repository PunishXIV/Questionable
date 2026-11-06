using System.Diagnostics;
using Questionable.Model.Questing;

namespace Questionable.Controller.Operations;

internal abstract class OwnedOperationProvider(OwnedOperationState operation) : IOperationProvider
{
    private readonly Stopwatch _statusPoll = new();

    public abstract int Order { get; }
    public abstract string Name { get; }
    public bool HasOperation => operation.HasOperation;
    public string? BlockReason => operation.BlockReason;
    public bool MovesCharacter => true;
    public bool CanUpdateWhileOccupied => true;
    public virtual bool HandlesLeadInTravel => false;

    protected abstract string CancelLabel { get; }
    public virtual bool HandlesPlayerDeath(ITaskExecutor? executor) => false;
    public abstract bool MayBeMovingCharacter();
    public virtual bool HandlesTravel(QuestStep step) => false;
    protected abstract bool IsOwningExecutor(ITaskExecutor? executor);
    protected abstract void PollOperation();
    protected abstract void CancelOwned();
    protected abstract void ObserveIdle();
    public abstract string? GetStartBlockReason();
    public abstract OperationStatusView? GetStatusView();
    public abstract OperationPill? GetTitleBarPill();

    protected OperationStatusView? StatusView(string checkText, string activeText, string confirmLabel,
        params string[] instructions)
    {
        if (!operation.HasOperation)
            return null;

        string status = operation.RequiresReconciliation || operation.IsAbandoned
            ? operation.BlockReason ?? checkText
            : activeText;
        return new(status, operation.RequiresReconciliation
            ? new OperationRecoveryView(confirmLabel, operation.CanConfirmIdle, instructions)
            : null);
    }

    protected OperationPill? TitleBarPill(string checkText, string activeText, string stoppingText)
    {
        if (operation.RequiresReconciliation)
            return new(checkText, Warning: true, NeedsCheck: true);
        if (!operation.HasOperation)
            return null;
        return operation.IsAbandoned
            ? new(stoppingText, Warning: true, NeedsCheck: false)
            : new(activeText, Warning: false, NeedsCheck: false);
    }

    public bool OwnsExecutor(ITaskExecutor? executor) => IsOwningExecutor(executor) && operation.HasOperation;

    public bool CanUpdateTask(ITaskExecutor? executor) =>
        !operation.HasOperation ||
        !operation.RequiresReconciliation && !operation.IsAbandoned && IsOwningExecutor(executor);

    public bool Update(IExternalOperationHost host, IOperationContext context)
    {
        bool hasMatchingExecutor = IsOwningExecutor(host.CurrentExecutor);
        if (!operation.HasOperation)
        {
            _statusPoll.Reset();
            return false;
        }

        // Stop scheduling before polling can release an abandoned operation at terminal status.
        if ((operation.IsAbandoned || operation.RequiresReconciliation || !hasMatchingExecutor) && host.IsActive)
            host.Stop("External operation no longer active");

        if (!_statusPoll.IsRunning || _statusPoll.Elapsed >= TimeSpan.FromMilliseconds(250))
        {
            _statusPoll.Restart();
            if (!hasMatchingExecutor)
                context.TryCleanup(CancelOwned, "detached external operation");
            if (!context.TryCleanup(PollOperation, "external operation status"))
            {
                host.Stop("External operation status unavailable");
                host.Status = operation.BlockReason ?? _L("Unable to observe the external operation.");
                return true;
            }
        }

        if (!operation.HasOperation)
        {
            host.Status = context.BlockReason;
            return false;
        }

        if (!operation.RequiresReconciliation && !operation.IsAbandoned && hasMatchingExecutor)
            return false;

        if (host.IsActive)
            host.Stop("External operation requires reconciliation");

        host.Status = operation.BlockReason;
        return true;
    }

    public void Abandon()
    {
    }

    public bool Cancel(IOperationContext context) => context.TryCleanup(CancelOwned, CancelLabel);

    public bool TryConfirmIdle(IExternalOperationHost host, IOperationContext context)
    {
        if (!operation.RequiresReconciliation || host.IsActive)
            return false;

        // Refresh both, so a newly running operation isn't confirmed from a stale status.
        if (!context.TryCleanup(PollOperation, "external operation confirmation status") ||
            !context.TryCleanup(ObserveIdle, "external operation idle confirmation"))
            return false;

        // Polling may already have released an abandoned operation on a terminal result.
        if (operation.HasOperation && !operation.TryConfirmIdle())
            return false;

        host.Status = context.BlockReason;
        return true;
    }
}
