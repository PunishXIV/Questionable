using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;

namespace Questionable.Controller.Operations;

// Artisan cannot identify or cancel requests, so uncertain work requires user confirmation.
[RegisterSingleton<IOperationProvider, ArtisanOperationProvider>(Duplicate = DuplicateStrategy.Append)]
internal sealed class ArtisanOperationProvider(
    IArtisanIpc artisanIpc,
    CraftingOperationState operation,
    ICondition condition,
    ILogger<ArtisanOperationProvider> logger) : IOperationProvider
{
    private readonly Stopwatch _statusPoll = new();

    public int Order => 100;
    public string Name => "Artisan";
    public bool HasOperation => operation.HasOperation;
    public string? BlockReason => operation.BlockReason;
    public bool MovesCharacter => false;
    public bool CanUpdateWhileOccupied => condition[ConditionFlag.Crafting];
    public bool HandlesLeadInTravel => false;

    public bool HandlesPlayerDeath(ITaskExecutor? executor) => false;
    public bool MayBeMovingCharacter() => false;
    public bool HandlesTravel(QuestStep step) => false;

    public bool OwnsExecutor(ITaskExecutor? executor) => executor is Craft.DoCraft && operation.HasOperation;

    public bool CanUpdateTask(ITaskExecutor? executor) =>
        !operation.HasOperation || !operation.RequiresReconciliation && executor is Craft.DoCraft;

    public bool Update(IExternalOperationHost host, IOperationContext context)
    {
        if (!operation.HasOperation)
        {
            _statusPoll.Reset();
            return false;
        }

        if (!_statusPoll.IsRunning || _statusPoll.Elapsed >= TimeSpan.FromMilliseconds(250))
            ObserveStatus();

        if (host.CurrentExecutor is not Craft.DoCraft)
            operation.AbandonCurrent();

        if (!operation.RequiresReconciliation)
            return false;

        if (host.IsActive)
            host.Stop("Artisan needs checking");

        host.Status = operation.BlockReason;
        return true;
    }

    public void Abandon() => operation.AbandonCurrent();

    public bool Cancel(IOperationContext context) => true;

    public bool TryConfirmIdle(IExternalOperationHost host, IOperationContext context)
    {
        if (!operation.RequiresReconciliation || host.IsActive)
            return false;

        ObserveStatus();
        if (!operation.TryConfirmIdle())
            return false;

        host.Status = context.BlockReason;
        return true;
    }

    public string? GetStartBlockReason() => null;

    public OperationStatusView? GetStatusView()
    {
        if (!operation.HasOperation)
            return null;

        return new(operation.RequiresReconciliation
                ? operation.BlockReason ?? _L("Check Artisan before restarting.")
                : _L("Crafting with Artisan"),
            operation.RequiresReconciliation
                ? new OperationRecoveryView(_L("I stopped Artisan"), operation.CanConfirmIdle,
                [
                    _L("Finish or cancel all queued work in Artisan and clear any pause or stop request there. Paused work is not idle."),
                    _L("Artisan must remain idle for at least 2 seconds. Confirmation only clears the block; start questing separately."),
                ])
                : null);
    }

    public OperationPill? GetTitleBarPill() =>
        operation.RequiresReconciliation ? new(_L("Check Artisan"), Warning: true, NeedsCheck: true) : null;

    private void ObserveStatus()
    {
        _statusPoll.Restart();
        try
        {
            operation.ObserveStatus(artisanIpc.GetStatus());
        }
        catch (Exception e)
        {
            operation.ObserveStatus(ArtisanStatus.Unavailable);
            logger.LogWarning(e, "Unable to observe retained Artisan operation");
        }
    }
}
