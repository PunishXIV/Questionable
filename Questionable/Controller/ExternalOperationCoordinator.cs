using System.Diagnostics;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;

namespace Questionable.Controller;

/// <summary>Coordinates retained provider work. Request creation and provider recovery rules stay with their owners.</summary>
[RegisterSingleton]
internal sealed class ExternalOperationCoordinator
{
    private readonly AutoDutyIpc _autoDutyIpc;
    private readonly BossModIpc _bossModIpc;
    private readonly Stopwatch _autoDutyStatusPoll = new();
    private readonly Stopwatch _combatStatusPoll = new();
    private readonly IArtisanIpc _artisanIpc;
    private readonly CraftingOperationState _craftingOperation;
    private readonly Stopwatch _craftingStatusPoll = new();
    private readonly IKnightshopperIpc _knightshopperIpc;
    private readonly PurchaseOperationState _purchaseOperation;
    private readonly Stopwatch _purchaseStatusPoll = new();
    private readonly IGatherChillIpc _gatherChillIpc;
    private readonly GatheringOperationState _gatheringOperation;
    private readonly Stopwatch _gatheringStatusPoll = new();
    private readonly IFeeshIpc _feeshIpc;
    private readonly FishingOperationState _fishingOperation;
    private readonly Stopwatch _fishingStatusPoll = new();
    private readonly CombatController _combatController;
    private readonly GatheringController _gatheringController;
    private readonly MovementController _movementController;
    private readonly ILogger<ExternalOperationCoordinator> _logger;

    public ExternalOperationCoordinator(
        AutoDutyIpc autoDutyIpc,
        BossModIpc bossModIpc,
        IArtisanIpc artisanIpc,
        CraftingOperationState craftingOperation,
        IKnightshopperIpc knightshopperIpc,
        PurchaseOperationState purchaseOperation,
        IGatherChillIpc gatherChillIpc,
        GatheringOperationState gatheringOperation,
        IFeeshIpc feeshIpc,
        FishingOperationState fishingOperation,
        CombatController combatController,
        GatheringController gatheringController,
        MovementController movementController,
        ILogger<ExternalOperationCoordinator> logger)
    {
        _autoDutyIpc = autoDutyIpc;
        _bossModIpc = bossModIpc;
        _artisanIpc = artisanIpc;
        _craftingOperation = craftingOperation;
        _knightshopperIpc = knightshopperIpc;
        _purchaseOperation = purchaseOperation;
        _gatherChillIpc = gatherChillIpc;
        _gatheringOperation = gatheringOperation;
        _feeshIpc = feeshIpc;
        _fishingOperation = fishingOperation;
        _combatController = combatController;
        _gatheringController = gatheringController;
        _movementController = movementController;
        _logger = logger;
    }

    public bool HasExternalOperation => _craftingOperation.HasOperation || _purchaseOperation.HasOperation ||
        _gatheringOperation.HasOperation || _fishingOperation.HasOperation || _autoDutyIpc.HasOperation ||
        _combatController.HasPendingCleanup || _bossModIpc.HasPendingCleanup;
    public string? BlockReason => _craftingOperation.BlockReason ?? _purchaseOperation.BlockReason ??
        _gatheringOperation.BlockReason ?? _fishingOperation.BlockReason ?? _autoDutyIpc.BlockReason ??
        _combatController.BlockReason ?? (_bossModIpc.HasPendingCleanup ? _bossModIpc.CleanupReason : null);

    public bool HasOwnedSoloDuty => _bossModIpc.HasOwnedSoloDuty;
    public bool OwnsCraft(ITaskExecutor? executor) => executor is Craft.DoCraft && _craftingOperation.HasOperation;
    public bool OwnsPurchase(ITaskExecutor? executor) => executor is Purchase.DoBuy && _purchaseOperation.HasOperation;
    public bool OwnsGathering(ITaskExecutor? executor) => executor is Gather.StartGathering && _gatheringOperation.HasOperation;
    public bool OwnsFishing(ITaskExecutor? executor) => executor is Fish.DoFish && _fishingOperation.HasOperation;
    public bool OwnsDuty(ITaskExecutor? executor) => executor is Duty.StartAutoDutyExecutor && _autoDutyIpc.HasOperation;

    public bool Update(IExternalOperationHost host)
    {
        // Reconcile retained work even while stopped, occupied or logged out.
        bool craftingBlocked = UpdateCraftingOperation(host);
        bool purchaseBlocked = UpdateOwnedOperation(host, _purchaseOperation, host.CurrentExecutor is Purchase.DoBuy,
            _purchaseStatusPoll, _knightshopperIpc.PollOperation, _knightshopperIpc.CancelOwned);
        bool gatheringBlocked = UpdateOwnedOperation(host, _gatheringOperation, host.CurrentExecutor is Gather.StartGathering,
            _gatheringStatusPoll, _gatherChillIpc.PollOperation, _gatherChillIpc.CancelOwned);
        bool fishingBlocked = UpdateOwnedOperation(host, _fishingOperation, host.CurrentExecutor is Fish.DoFish,
            _fishingStatusPoll, _feeshIpc.PollOperation, _feeshIpc.CancelOwned);
        bool dutyBlocked = UpdateOwnedOperation(host, _autoDutyIpc.Operation, host.CurrentExecutor is Duty.StartAutoDutyExecutor,
            _autoDutyStatusPoll, _autoDutyIpc.PollOperation, _autoDutyIpc.CancelOwned);
        bool combatBlocked = UpdateCombatRecovery(host);
        return craftingBlocked || purchaseBlocked || gatheringBlocked || fishingBlocked || dutyBlocked || combatBlocked;
    }

    public bool CancelAll(Func<bool> resetQueue)
    {
        // Provider work is cleaned up even when its executor has already detached.
        _craftingOperation.AbandonCurrent();
        bool success = resetQueue();
        success &= TryCleanup(_knightshopperIpc.CancelOwned, "Knightshopper purchase");
        success &= TryCleanup(_feeshIpc.CancelOwned, "Feesh fishing");
        if (_movementController.Destination != null || _purchaseOperation.HasOperation ||
            _gatheringOperation.HasOperation || _fishingOperation.HasOperation || _autoDutyIpc.HasOperation)
            success &= TryCleanup(_movementController.Stop, "movement");
        success &= TryCleanup(() => _combatController.Stop("ClearTasksInternal"), "combat");
        success &= TryCleanup(() => _gatheringController.Stop("ClearTasksInternal"), "gathering");
        success &= TryCleanup(_autoDutyIpc.Stop, "AutoDuty");
        success &= TryCleanup(_bossModIpc.DisableSoloDutyPreset, "solo duty");
        // A provider can still be cleaning up without having failed; don't queue replacements yet.
        return success && !HasExternalOperation;
    }

    private bool TryCleanup(Action cleanup, string operation)
    {
        try
        {
            cleanup();
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to stop {Operation}", operation);
            return false;
        }
    }

    public bool CanPrepareQuestTasks(out string? reason)
    {
        reason = null;
        if (HasExternalOperation)
            return false;

        if (_autoDutyIpc.IsAvailable() && _autoDutyIpc.GetStopped() != true)
        {
            reason = _L("AutoDuty is busy or its status is unavailable. Finish its current work before starting questing.");
            return false;
        }

        // Manual Knightshopper work has no owner here, so block on busy or unreadable status.
        if (_knightshopperIpc.IsReady() && _knightshopperIpc.GetBusy() != false)
        {
            reason = _L("Knightshopper is busy or its status is unavailable. Finish its current work before starting questing.");
            return false;
        }

        if (_gatherChillIpc.IsAvailable() && _gatherChillIpc.GetBusy() != false)
        {
            reason = _L("Gather n Chill is busy or its status is unavailable. Finish its current work before starting questing.");
            return false;
        }

        // WahTools drives the character while busy.
        if (_feeshIpc.IsAvailable() && _feeshIpc.GetBusy() != false)
        {
            reason = _L("WahTools is busy or its status is unavailable. Finish its current work before starting questing.");
            return false;
        }

        return true;
    }

    private bool UpdateCraftingOperation(IExternalOperationHost host)
    {
        if (!_craftingOperation.HasOperation)
        {
            _craftingStatusPoll.Reset();
            return false;
        }

        if (!_craftingStatusPoll.IsRunning || _craftingStatusPoll.Elapsed >= TimeSpan.FromMilliseconds(250))
            ObserveCraftingStatus();

        if (host.CurrentExecutor is not Craft.DoCraft)
            _craftingOperation.AbandonCurrent();

        if (!_craftingOperation.RequiresReconciliation)
            return false;

        if (host.IsActive)
            host.Stop("Artisan requires reconciliation");

        host.Status = _craftingOperation.BlockReason;
        return true;
    }

    private void ObserveCraftingStatus()
    {
        _craftingStatusPoll.Restart();
        try
        {
            _craftingOperation.ObserveStatus(_artisanIpc.GetStatus());
        }
        catch (Exception e)
        {
            _craftingOperation.ObserveStatus(ArtisanStatus.Unavailable);
            _logger.LogWarning(e, "Unable to observe retained Artisan operation");
        }
    }

    public bool TryConfirmArtisanIdle(IExternalOperationHost host)
    {
        // Confirming only releases the barrier; it never resumes questing.
        if (!_craftingOperation.RequiresReconciliation || host.IsActive)
            return false;

        ObserveCraftingStatus();
        if (!_craftingOperation.TryConfirmIdle())
            return false;

        host.Status = BlockReason;
        return true;
    }

    private bool UpdateOwnedOperation(IExternalOperationHost host, OwnedOperationState operation, bool hasMatchingExecutor,
        Stopwatch statusPoll, Action poll, Action cancel)
    {
        if (!operation.HasOperation)
        {
            statusPoll.Reset();
            return false;
        }

        // Stop scheduling before polling can release an abandoned operation at terminal status.
        if ((operation.IsAbandoned || operation.RequiresReconciliation || !hasMatchingExecutor) && host.IsActive)
            host.Stop("External operation no longer active");

        if (!statusPoll.IsRunning || statusPoll.Elapsed >= TimeSpan.FromMilliseconds(250))
        {
            statusPoll.Restart();
            if (!hasMatchingExecutor)
                TryCleanup(cancel, "detached external operation");
            if (!TryCleanup(poll, "external operation status"))
            {
                host.Stop("External operation status unavailable");
                host.Status = operation.BlockReason ?? _L("Unable to observe the external operation.");
                return true;
            }
        }

        if (!operation.HasOperation)
        {
            host.Status = BlockReason;
            return false;
        }

        if (!operation.RequiresReconciliation && !operation.IsAbandoned && hasMatchingExecutor)
            return false;

        if (host.IsActive)
            host.Stop("External operation requires reconciliation");

        host.Status = operation.BlockReason;
        return true;
    }

    public bool TryConfirmKnightshopperIdle(IExternalOperationHost host) =>
        TryConfirmOwnedOperationIdle(host, _purchaseOperation, _knightshopperIpc.PollOperation, _knightshopperIpc.ObserveIdle);

    public bool TryConfirmGatherChillIdle(IExternalOperationHost host) =>
        TryConfirmOwnedOperationIdle(host, _gatheringOperation, _gatherChillIpc.PollOperation, _gatherChillIpc.ObserveIdle);

    public bool TryConfirmFeeshIdle(IExternalOperationHost host) =>
        TryConfirmOwnedOperationIdle(host, _fishingOperation, _feeshIpc.PollOperation, _feeshIpc.ObserveIdle);

    public bool TryConfirmAutoDutyIdle(IExternalOperationHost host) =>
        TryConfirmOwnedOperationIdle(host, _autoDutyIpc.Operation, _autoDutyIpc.PollOperation, _autoDutyIpc.ObserveIdle);

    public bool TryConfirmBossModStopped(IExternalOperationHost host)
    {
        if (host.IsActive || !_bossModIpc.TryConfirmStopped())
            return false;
        _combatController.TryRetryCleanup();
        host.Status = BlockReason;
        return true;
    }

    private bool UpdateCombatRecovery(IExternalOperationHost host)
    {
        if (_combatController.HasPendingCleanup || _bossModIpc.HasPendingCleanup)
        {
            // Clear scheduling before a cleanup retry can release the retained owner.
            if (host.IsActive)
                host.Stop("Combat cleanup is unresolved");
        }

        if (!_combatStatusPoll.IsRunning || _combatStatusPoll.Elapsed >= TimeSpan.FromMilliseconds(250))
        {
            _combatStatusPoll.Restart();
            _combatController.TryRetryCleanup();
            try
            {
                // Solo duties keep local movement/interaction tasks between BossMod activations.
                _bossModIpc.PollSoloDuty();
            }
            catch (Exception e)
            {
                if (host.IsActive)
                {
                    _logger.LogWarning(e, "Solo-duty control was lost");
                    host.Stop("Solo-duty control was lost");
                }
            }
        }

        if (!_combatController.HasPendingCleanup && !_bossModIpc.HasPendingCleanup)
            return false;
        host.Status = BlockReason;
        return true;
    }

    private bool TryConfirmOwnedOperationIdle(IExternalOperationHost host, OwnedOperationState operation, Action poll, Action observeIdle)
    {
        if (!operation.RequiresReconciliation || host.IsActive)
            return false;

        // Refresh both, so a newly running operation isn't confirmed from a stale status.
        if (!TryCleanup(poll, "external operation confirmation status") ||
            !TryCleanup(observeIdle, "external operation idle confirmation"))
            return false;

        // Polling may already have released an abandoned operation on a terminal result.
        if (operation.HasOperation && !operation.TryConfirmIdle())
            return false;

        host.Status = BlockReason;
        return true;
    }

    public bool CanUpdateTask(ITaskExecutor? executor)
    {
        if (_craftingOperation.HasOperation &&
            (_craftingOperation.RequiresReconciliation || executor is not Craft.DoCraft))
            return false;

        if (_purchaseOperation.HasOperation &&
            (_purchaseOperation.RequiresReconciliation || _purchaseOperation.IsAbandoned ||
             executor is not Purchase.DoBuy))
            return false;

        if (_gatheringOperation.HasOperation &&
            (_gatheringOperation.RequiresReconciliation || _gatheringOperation.IsAbandoned ||
             executor is not Gather.StartGathering))
            return false;

        if (_fishingOperation.HasOperation &&
            (_fishingOperation.RequiresReconciliation || _fishingOperation.IsAbandoned ||
             executor is not Fish.DoFish))
            return false;

        if (_autoDutyIpc.HasOperation &&
            (_autoDutyIpc.RequiresReconciliation || _autoDutyIpc.Operation.IsAbandoned ||
             executor is not Duty.StartAutoDutyExecutor))
            return false;

        if (_combatController.HasPendingCleanup || _bossModIpc.HasPendingCleanup)
            return false;

        return true;
    }
}
