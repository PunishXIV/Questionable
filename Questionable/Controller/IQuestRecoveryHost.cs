using Questionable.Model.Questing;

namespace Questionable.Controller;

/// <summary>Live state and effects used by quest stop and death recovery.</summary>
internal interface IQuestRecoveryHost
{
    bool IsManual { get; }
    bool IsQuestWindowOpen { get; }
    bool HasQueuedTasks { get; }
    bool IsLoggedIn { get; }
    bool EscapeRequested { get; }
    bool IsUnconscious { get; }
    bool OwnsDuty { get; }
    bool HasOwnedSoloDuty { get; }
    bool HasExternalOperation { get; }
    bool IsZoning { get; }
    bool HasPlayer { get; }
    (ElementId QuestId, byte Sequence, int Step)? CurrentStep { get; }

    bool ClearTasks();
    void StopMovement();
    bool TryConfirmReturnPrompt();
    void Stop(string reason);
    void StopAll(string reason);
    void ReportDeathLimit(int deaths);
    void RetryCurrentStep();
}
