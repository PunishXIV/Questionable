using System.Diagnostics;
using Questionable.Model.Questing;

namespace Questionable.Controller.Operations;

// Combat and BossMod solo-duty work can outlive a stop until released.
[RegisterSingleton<IOperationProvider, CombatCleanupProvider>(Duplicate = DuplicateStrategy.Append)]
internal sealed class CombatCleanupProvider(
    CombatController combatController,
    BossModIpc bossModIpc,
    ILogger<CombatCleanupProvider> logger) : IOperationProvider
{
    private readonly Stopwatch _statusPoll = new();

    public int Order => 600;
    public string Name => "Combat cleanup";
    public bool HasOperation => combatController.HasPendingCleanup || bossModIpc.HasPendingCleanup;

    public string? BlockReason =>
        combatController.BlockReason ?? (bossModIpc.HasPendingCleanup ? bossModIpc.CleanupReason : null);

    public bool MovesCharacter => false;
    public bool CanUpdateWhileOccupied => false;
    public bool HandlesLeadInTravel => false;

    public bool OwnsExecutor(ITaskExecutor? executor) => false;

    // BossMod owns death and retry behaviour during a solo duty it runs for us.
    public bool HandlesPlayerDeath(ITaskExecutor? executor) => bossModIpc.HasOwnedSoloDuty;

    public bool MayBeMovingCharacter() => false;
    public bool HandlesTravel(QuestStep step) => false;

    public bool CanUpdateTask(ITaskExecutor? executor) => !HasOperation;

    public bool Update(IExternalOperationHost host, IOperationContext context)
    {
        // Clear scheduling before a cleanup retry can release the retained owner.
        if (HasOperation && host.IsActive)
            host.Stop("Combat cleanup is unresolved");

        if (!_statusPoll.IsRunning || _statusPoll.Elapsed >= TimeSpan.FromMilliseconds(250))
        {
            _statusPoll.Restart();
            combatController.TryRetryCleanup();
            try
            {
                // Solo duties keep local movement/interaction tasks between BossMod activations.
                bossModIpc.PollSoloDuty();
            }
            catch (Exception e)
            {
                if (host.IsActive)
                {
                    logger.LogWarning(e, "Solo-duty control was lost");
                    host.Stop("Solo-duty control was lost");
                }
            }
        }

        if (!HasOperation)
            return false;
        host.Status = context.BlockReason;
        return true;
    }

    public void Abandon()
    {
    }

    public bool Cancel(IOperationContext context)
    {
        bool success = context.TryCleanup(() => combatController.Stop("ClearTasksInternal"), "combat");
        success &= context.TryCleanup(bossModIpc.DisableSoloDutyPreset, "solo duty");
        return success;
    }

    public bool TryConfirmIdle(IExternalOperationHost host, IOperationContext context)
    {
        if (host.IsActive || !bossModIpc.TryConfirmStopped())
            return false;
        combatController.TryRetryCleanup();
        host.Status = context.BlockReason;
        return true;
    }

    public string? GetStartBlockReason() => null;

    public OperationStatusView? GetStatusView()
    {
        if (!HasOperation)
            return null;

        return new(bossModIpc.CleanupReason ?? combatController.BlockReason ??
                   _L("Waiting for combat control to be released."),
            bossModIpc.HasPendingCleanup
                ? new OperationRecoveryView(_L("I stopped BossMod"), bossModIpc.CanConfirmStopped,
                    [_L("Stop BossMod's active presets and check its settings before confirming. Confirmation does not restart questing.")])
                : null);
    }

    // The title bar shows combat itself, from CombatController.IsRunning.
    public OperationPill? GetTitleBarPill() => null;
}
