using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc.Exceptions;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class ArtisanIpc : Ipc, IDisposable
{
    private readonly ILogger<ArtisanIpc> _logger;
    private readonly CraftingState _operation;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("CraftItem")] private readonly Action<ushort, int> _craftItem = null!;
    [EzIPC("GetLists")] private readonly Func<Dictionary<int, string>> _getLists = null!;
    [EzIPC("GetStopRequest")] private readonly Func<bool> _getStopRequest = null!;
    [EzIPC("IsListPaused")] private readonly Func<bool> _isListPaused = null!;
    [EzIPC("IsListRunning")] private readonly Func<bool> _isListRunning = null!;
    [EzIPC("GetEnduranceStatus")] private readonly Func<bool> _getEnduranceStatus = null!;
    [EzIPC("StartListById")] private readonly Action<int> _startListById = null!;
    [EzIPC("IsBusy")] private readonly Func<bool> _isBusy = null!;

    public ArtisanIpc(ILogger<ArtisanIpc> logger, CraftingState operation)
    {
        _logger = logger;
        _operation = operation;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
    }

    public override string InternalName => "Artisan";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => !_operation.HasOperation && GetStatus() == ArtisanStatus.Idle;

    public ArtisanStartResult CraftItem(ushort recipeId, int crafts)
    {
        if (recipeId == 0 || crafts <= 0)
            return ArtisanStartResult.Failed;

        ArtisanStartResult? rejection = CheckReady();
        if (rejection.HasValue)
            return rejection.Value;

        Guid? operationId = null;
        try
        {
            _logger.LogInformation("Submitting {Crafts} crafts with recipe {RecipeId} to Artisan", crafts,
                recipeId);
            operationId = _operation.BeginRequest();
            _logger.LogDebug("Tracking Artisan recipe submission {Id}", operationId);
            _craftItem(recipeId, crafts);
            return ArtisanStartResult.Sent;
        }
        catch (IpcError e)
        {
            if (operationId.HasValue)
                _operation.MarkUncertain(operationId.Value);
            _logger.LogError(e, "Unable to craft items");
            return ArtisanStartResult.Unavailable;
        }
        catch (Exception e)
        {
            if (operationId.HasValue)
                _operation.MarkUncertain(operationId.Value);
            _logger.LogError(e, "Artisan recipe submission failed; it may have queued work, do not retry automatically");
            return ArtisanStartResult.Failed;
        }
    }

    public ArtisanStartResult CraftList(int listId)
    {
        ArtisanStartResult? rejection = CheckReady();
        if (rejection.HasValue)
            return rejection.Value;

        Guid? operationId = null;
        try
        {
            if (!_getLists().ContainsKey(listId))
                return ArtisanStartResult.MissingList;

            rejection = CheckReady();
            if (rejection.HasValue)
                return rejection.Value;

            _logger.LogInformation("Submitting list {ListId} to Artisan", listId);
            operationId = _operation.BeginRequest();
            _logger.LogDebug("Tracking Artisan list submission {Id}", operationId);
            _startListById(listId);
            return ArtisanStartResult.Sent;
        }
        catch (IpcError e)
        {
            if (operationId.HasValue)
                _operation.MarkUncertain(operationId.Value);
            _logger.LogError(e, "Unable to submit Artisan list");
            return ArtisanStartResult.Unavailable;
        }
        catch (Exception e)
        {
            if (operationId.HasValue)
                _operation.MarkUncertain(operationId.Value);
            _logger.LogError(e, "Artisan list submission failed; it may have queued work, do not fall back");
            return ArtisanStartResult.Failed;
        }
    }

    private ArtisanStartResult? CheckReady() => _operation.HasOperation ? ArtisanStartResult.Busy : GetStatus() switch
    {
        ArtisanStatus.Idle => null,
        ArtisanStatus.Unavailable => ArtisanStartResult.Unavailable,
        _ => ArtisanStartResult.Busy,
    };

    public ArtisanStatus GetStatus()
    {
        try
        {
            // Never clear another caller's stop request or resume its paused list.
            if (_getStopRequest() || _isListPaused())
                return ArtisanStatus.Paused;
            if (_isListRunning() || _getEnduranceStatus())
                return ArtisanStatus.Busy;
            return _isBusy() ? ArtisanStatus.Queued : ArtisanStatus.Idle;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to check Artisan status");
            return ArtisanStatus.Unavailable;
        }
    }

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}

internal enum ArtisanStartResult
{
    // Artisan's IPC returns nothing, so Sent doesn't mean it ran.
    Sent,
    MissingList,
    Busy,
    Unavailable,
    Failed,
}

internal enum ArtisanStatus
{
    Unavailable,
    Idle,
    // Busy with setup/cleanup, but neither a list nor endurance is active yet.
    Queued,
    Busy,
    Paused,
}

// Artisan cannot identify or cancel requests, so uncertain work requires user confirmation.
[RegisterSingleton<IOperationProvider, ArtisanProvider>(Duplicate = DuplicateStrategy.Append)]
internal sealed class ArtisanProvider(
    ArtisanIpc artisanIpc,
    CraftingState operation,
    ICondition condition,
    ILogger<ArtisanProvider> logger) : IOperationProvider
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

    public bool HandlesExecutor(ITaskExecutor? executor) => executor is Craft.DoCraft && operation.HasOperation;

    public bool CanUpdateTask(ITaskExecutor? executor) =>
        !operation.HasOperation || !operation.NeedsConfirmation && executor is Craft.DoCraft;

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
            operation.Abandon();

        if (!operation.NeedsConfirmation)
            return false;

        if (host.IsActive)
            host.Stop("Artisan needs checking");

        host.Status = operation.BlockReason;
        return true;
    }

    public void Abandon() => operation.Abandon();

    public bool Cancel(IOperationContext context) => true;

    public bool TryConfirmIdle(IExternalOperationHost host, IOperationContext context)
    {
        if (!operation.NeedsConfirmation || host.IsActive)
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

        return new(operation.NeedsConfirmation
                ? operation.BlockReason ?? _L("Check Artisan before restarting.")
                : _L("Crafting with Artisan"),
            operation.NeedsConfirmation
                ? new OperationRecoveryView(_L("I stopped Artisan"), operation.CanConfirmIdle,
                [
                    _L("Finish or cancel all queued work in Artisan and clear any pause or stop request there. Paused work is not idle."),
                    _L("Artisan must remain idle for at least 2 seconds. Confirmation only clears the block; start questing separately."),
                ])
                : null);
    }

    public OperationPill? GetTitleBarPill() =>
        operation.NeedsConfirmation ? new(_L("Check Artisan"), Warning: true, NeedsCheck: true) : null;

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

// Artisan cannot identify or cancel submissions, so uncertain work needs reconciliation.
[RegisterSingleton]
internal sealed class CraftingState(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long? _idleSince;
    private long? _lastObservedAt;
    private ArtisanStatus _status;

    public Guid? Id { get; private set; }
    public bool HasOperation => Id.HasValue;
    public bool NeedsConfirmation { get; private set; }

    public string? BlockReason => !HasOperation ? null : NeedsConfirmation
        ? "Artisan may still have crafting work. Finish or stop it in Artisan, then confirm it is stopped."
        : "Artisan is handling a crafting request. Stop Questionable before starting other work.";

    public bool CanConfirmIdle => HasOperation && NeedsConfirmation && IdleSettled;

    private bool IdleSettled => _status == ArtisanStatus.Idle && _idleSince is { } idleSince &&
        _lastObservedAt is { } observedAt &&
        _clock.GetElapsedTime(idleSince) >= TimeSpan.FromSeconds(2) &&
        _clock.GetElapsedTime(observedAt) <= TimeSpan.FromSeconds(1);

    public Guid BeginRequest()
    {
        if (HasOperation)
            throw new TaskException(BlockReason!);

        Id = Guid.NewGuid();
        NeedsConfirmation = false;
        _idleSince = null;
        _lastObservedAt = null;
        _status = ArtisanStatus.Queued;
        return Id.Value;
    }

    public void MarkUncertain(Guid operationId)
    {
        if (Id == operationId)
            Abandon();
    }

    public void Abandon()
    {
        if (!HasOperation)
            return;

        NeedsConfirmation = true;
    }

    public void Abandon(Guid? operationId)
    {
        if (operationId.HasValue && Id == operationId)
            Abandon();
    }

    public void ObserveStatus(ArtisanStatus status)
    {
        if (!HasOperation)
            return;

        long now = _clock.GetTimestamp();
        if (_lastObservedAt is { } previous && _clock.GetElapsedTime(previous, now) > TimeSpan.FromSeconds(1))
            _idleSince = null;

        _status = status;
        _lastObservedAt = now;
        if (status == ArtisanStatus.Idle)
            _idleSince ??= _lastObservedAt;
        else
            _idleSince = null;

        // A later idle response cannot prove that a replacement provider finished our request.
        if (status == ArtisanStatus.Unavailable)
            NeedsConfirmation = true;
    }

    public bool CanContinue(Guid? operationId) => operationId.HasValue && Id == operationId &&
        !NeedsConfirmation;

    public bool TryComplete(Guid? operationId)
    {
        if (!CanContinue(operationId) || !IdleSettled)
            return false;

        Clear();
        return true;
    }

    public bool TryConfirmIdle()
    {
        if (!CanConfirmIdle)
            return false;

        Clear();
        return true;
    }

    private void Clear()
    {
        Id = null;
        NeedsConfirmation = false;
        _idleSince = null;
        _lastObservedAt = null;
    }
}
