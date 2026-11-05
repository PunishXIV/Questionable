using Questionable.Model.Questing;

namespace Questionable.Controller.Operations;

/// <summary>
///     One plugin (or Questionable's own combat cleanup) whose work outlives the task that started it.
///     The coordinator loops over every registered provider, so adding one is a class plus its registration.
/// </summary>
internal interface IOperationProvider
{
    /// <summary>Lower runs first, and its block reason wins when several providers block.</summary>
    int Order { get; }

    string Name { get; }

    /// <summary>Retained work that blocks scheduling other tasks.</summary>
    bool HasOperation { get; }

    string? BlockReason { get; }

    /// <summary>Its work moves the character, so Questionable's own movement stops with it.</summary>
    bool MovesCharacter { get; }

    bool OwnsExecutor(ITaskExecutor? executor);

    bool CanUpdateTask(ITaskExecutor? executor);

    /// <summary>Whether the executor it owns keeps updating while the player is occupied.</summary>
    bool CanUpdateWhileOccupied { get; }

    /// <summary>Whether it handles the player's death itself, so Questionable's death recovery stays out.</summary>
    bool HandlesPlayerDeath(ITaskExecutor? executor);

    /// <summary>The plugin is busy (or its status is unreadable), so it may be moving the character for its own work.</summary>
    bool MayBeMovingCharacter();

    /// <summary>It travels to this step itself, so Questionable skips its own travel for it.</summary>
    bool HandlesTravel(QuestStep step);

    /// <summary>Walk-only steps that just lead to a step it handles are skipped too.</summary>
    bool HandlesLeadInTravel { get; }

    /// <summary>Reconciles retained work, even while stopped. True blocks the rest of the frame.</summary>
    bool Update(IExternalOperationHost host, IOperationContext context);

    /// <summary>Runs before the task queue is reset.</summary>
    void Abandon();

    /// <summary>Runs after the task queue is reset. False when cleanup failed.</summary>
    bool Cancel(IOperationContext context);

    /// <summary>Releases a reconciliation barrier once the user stopped the plugin. Never resumes questing.</summary>
    bool TryConfirmIdle(IExternalOperationHost host, IOperationContext context);

    /// <summary>Why questing can't start while the plugin does work Questionable doesn't own, or null.</summary>
    string? GetStartBlockReason();

    /// <summary>The status panel entry, or null when there is nothing to show.</summary>
    OperationStatusView? GetStatusView();

    /// <summary>The title-bar status, or null to leave it to other providers and Questionable.</summary>
    OperationPill? GetTitleBarPill();
}

/// <summary>What the coordinator lends a provider while it updates, cancels or confirms.</summary>
internal interface IOperationContext
{
    /// <summary>The block reason across all providers.</summary>
    string? BlockReason { get; }

    /// <summary>Runs a cleanup step, logging and returning false if it throws.</summary>
    bool TryCleanup(Action cleanup, string operation);
}

/// <summary>What the status panel shows for a provider's retained work. All text is already localized.</summary>
internal sealed record OperationStatusView(string Status, OperationRecoveryView? Recovery = null);

/// <summary>
///     Shown while the user has to stop the plugin and confirm before questing can continue.
///     <paramref name="Detail"/> goes above the button, <paramref name="Instructions"/> below it.
/// </summary>
internal sealed record OperationRecoveryView(
    string ConfirmLabel,
    bool CanConfirm,
    IReadOnlyList<string> Instructions,
    string? Detail = null);

/// <summary>
///     The title-bar status for a provider. Any provider that needs checking wins over every one that is just working.
/// </summary>
internal sealed record OperationPill(string Text, bool Warning, bool NeedsCheck);
