namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class ExternalOperationCoordinator : IOperationContext
{
    private readonly IReadOnlyList<IOperationProvider> _providers;
    private readonly MovementController _movementController;
    private readonly ILogger<ExternalOperationCoordinator> _logger;

    public ExternalOperationCoordinator(
        IEnumerable<IOperationProvider> providers,
        MovementController movementController,
        ILogger<ExternalOperationCoordinator> logger)
    {
        _providers = providers.OrderBy(x => x.Order).ToList();
        IOperationProvider? duplicate = _providers.GroupBy(x => x.Order).FirstOrDefault(x => x.Count() > 1)?.First();
        if (duplicate != null)
            throw new InvalidOperationException($"Operation providers share order {duplicate.Order}");
        _movementController = movementController;
        _logger = logger;
    }

    public IReadOnlyList<IOperationProvider> Providers => _providers;
    public bool HasExternalOperation => _providers.Any(x => x.HasOperation);
    public string? BlockReason => _providers.Select(x => x.BlockReason).FirstOrDefault(x => x != null);

    public bool HasMovingOperation => _providers.Any(x => x.MovesCharacter && x.HasOperation);

    public bool MayBeMovingCharacter() => _providers.Any(x => x.MayBeMovingCharacter());

    public IOperationProvider? GetOwner(ITaskExecutor? executor) => _providers.FirstOrDefault(x => x.OwnsExecutor(executor));

    public bool HandlesPlayerDeath(ITaskExecutor? executor) => _providers.Any(x => x.HandlesPlayerDeath(executor));

    public bool Update(IExternalOperationHost host)
    {
        // Reconcile retained work even while stopped, occupied or logged out. Each provider sees the executor
        // as earlier providers left it.
        bool blocked = false;
        foreach (IOperationProvider provider in _providers)
            blocked |= provider.Update(host, this);
        return blocked;
    }

    public bool CancelAll(Func<bool> resetQueue)
    {
        bool stopMovement = _movementController.Destination != null || HasMovingOperation;
        foreach (IOperationProvider provider in _providers)
            provider.Abandon();
        bool success = resetQueue();

        // Provider work is cleaned up even when its executor has already detached.
        foreach (IOperationProvider provider in _providers)
            success &= provider.Cancel(this);
        if (stopMovement)
            success &= TryCleanup(_movementController.Stop, "movement");

        // A provider can still be cleaning up without having failed; don't queue replacements yet.
        return success && !HasExternalOperation;
    }

    bool IOperationContext.TryCleanup(Action cleanup, string operation) => TryCleanup(cleanup, operation);

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

        reason = _providers.Select(x => x.GetStartBlockReason()).FirstOrDefault(x => x != null);
        return reason == null;
    }

    public bool TryConfirmIdle(IOperationProvider provider, IExternalOperationHost host) =>
        provider.TryConfirmIdle(host, this);

    public bool CanUpdateTask(ITaskExecutor? executor) => _providers.All(x => x.CanUpdateTask(executor));

    public OperationPill? GetTitleBarPill()
    {
        OperationPill?[] pills = _providers.Select(x => x.GetTitleBarPill()).ToArray();
        return pills.FirstOrDefault(x => x is { NeedsCheck: true }) ?? pills.FirstOrDefault(x => x != null);
    }
}
