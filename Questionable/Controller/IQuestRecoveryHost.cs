using Questionable.Model.Questing;

namespace Questionable.Controller;

internal interface IQuestRecoveryHost
{
    bool IsManual { get; }
    bool IsQuestWindowOpen { get; }
    bool HasQueuedTasks { get; }
    bool IsLoggedIn { get; }
    bool EscapeRequested { get; }
    bool IsUnconscious { get; }
    // AutoDuty and BossMod solo duties manage their own death and retry behavior.
    bool ProviderHandlesDeath { get; }
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
