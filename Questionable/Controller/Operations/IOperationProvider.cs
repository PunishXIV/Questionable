namespace Questionable.Controller.Operations;

internal interface IOperationProvider
{
    // Lower orders win when several providers block.
    int Order { get; }

    string Name { get; }

    bool HasOperation { get; }

    string? BlockReason { get; }

    bool MovesCharacter { get; }

    bool HandlesExecutor(ITaskExecutor? executor);

    bool CanUpdateTask(ITaskExecutor? executor);

    bool CanUpdateWhileOccupied { get; }

    bool HandlesPlayerDeath(ITaskExecutor? executor);

    bool MayBeMovingCharacter();

    bool HandlesTravel(QuestStep step);

    bool HandlesLeadInTravel { get; }

    bool Update(IExternalOperationHost host, IOperationContext context);

    void Abandon();

    bool Cancel(IOperationContext context);

    bool TryConfirmIdle(IExternalOperationHost host, IOperationContext context);

    string? GetStartBlockReason();

    OperationStatusView? GetStatusView();

    OperationPill? GetTitleBarPill();
}

internal interface IOperationContext
{
    string? BlockReason { get; }

    bool TryCleanup(Action cleanup, string operation);
}

internal sealed record OperationStatusView(string Status, OperationRecoveryView? Recovery = null);

internal sealed record OperationRecoveryView(
    string ConfirmLabel,
    bool CanConfirm,
    IReadOnlyList<string> Instructions,
    string? Detail = null);

internal sealed record OperationPill(string Text, bool Warning, bool NeedsCheck);
