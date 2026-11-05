using Questionable.Model.Questing;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class QuestRecoveryController(ILogger<QuestRecoveryController> logger, TimeProvider? timeProvider = null)
{
    private const int MaxConsecutiveDeaths = 5;
    private static readonly TimeSpan RespawnGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReturnConfirmInterval = TimeSpan.FromMilliseconds(500);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long? _respawnedAt;
    private long? _lastReturnConfirmAt;
    private (ElementId QuestId, byte Sequence, int Step)? _deathStreakKey;
    private int _deathStreakCount;

    public bool IsRecovering { get; private set; }

    public bool IsQuestingStopped(IQuestRecoveryHost host) =>
        host.IsManual && !host.HasQueuedTasks && !IsRecovering && !host.IsQuestWindowOpen;

    public bool Update(IQuestRecoveryHost host)
    {
        if (!host.IsLoggedIn)
        {
            Stop(host, "Logged out", all: true);
            return true;
        }

        if (host.EscapeRequested && (host.HasQueuedTasks || IsRecovering))
        {
            Stop(host, "ESC pressed", all: true);
            return true;
        }

        if (host.IsUnconscious)
        {
            // These providers own their death/retry behavior.
            if (host.OwnsDuty || host.HasOwnedSoloDuty)
                return false;

            if (host.HasQueuedTasks || IsRecovering)
            {
                BeginRecovery(host);
                return true;
            }
        }
        else if (IsRecovering)
        {
            FinishRecovery(host);
            return true;
        }

        return false;
    }

    public void Reset()
    {
        IsRecovering = false;
        _respawnedAt = null;
        _lastReturnConfirmAt = null;
        _deathStreakKey = null;
        _deathStreakCount = 0;
    }

    private void BeginRecovery(IQuestRecoveryHost host)
    {
        if (host.HasExternalOperation)
        {
            Stop(host, "Died during external operation", all: false);
            return;
        }

        if (!IsRecovering)
        {
            IsRecovering = true;
            _respawnedAt = null;
            _lastReturnConfirmAt = null;
            logger.LogWarning("Player died while questing — waiting to return and retry the current step");
            if (!host.ClearTasks())
            {
                Stop(host, "Task cleanup failed", all: false);
                return;
            }
            host.StopMovement();
        }

        if (_lastReturnConfirmAt is { } confirmedAt && _clock.GetElapsedTime(confirmedAt) < ReturnConfirmInterval)
            return;

        if (host.TryConfirmReturnPrompt())
            _lastReturnConfirmAt = _clock.GetTimestamp();
    }

    private void FinishRecovery(IQuestRecoveryHost host)
    {
        if (!host.IsLoggedIn || host.IsZoning || !host.HasPlayer)
        {
            _respawnedAt = null;
            return;
        }

        if (_respawnedAt is not { } respawnedAt)
        {
            _respawnedAt = _clock.GetTimestamp();
            return;
        }

        if (_clock.GetElapsedTime(respawnedAt) < RespawnGrace)
            return;

        IsRecovering = false;
        var step = host.CurrentStep;
        if (_deathStreakKey != step)
        {
            _deathStreakKey = step;
            _deathStreakCount = 0;
        }
        int deaths = ++_deathStreakCount;
        if (deaths >= MaxConsecutiveDeaths)
        {
            logger.LogError("Player died {Deaths} times on the same step — stopping", deaths);
            host.ReportDeathLimit(MaxConsecutiveDeaths);
            Stop(host, "Died too many times", all: true);
            return;
        }

        logger.LogInformation("Retrying current step after death ({Deaths}/{Max})", deaths, MaxConsecutiveDeaths);
        host.RetryCurrentStep();
    }

    private void Stop(IQuestRecoveryHost host, string reason, bool all)
    {
        Reset();
        if (all)
            host.StopAll(reason);
        else
            host.Stop(reason);
    }
}
