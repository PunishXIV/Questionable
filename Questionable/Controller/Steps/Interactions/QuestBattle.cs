using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using static Questionable.Controller.Steps.ITaskExecutor;
namespace Questionable.Controller.Steps.Interactions;

internal static class QuestBattle
{
    internal static int AttemptLimit(bool retry, int maxAttempts) => retry ? Math.Clamp(maxAttempts, 2, 10) : 1;

    internal sealed class RetryCounter
    {
        private (string ContentId, ElementId QuestId, uint CfcId, byte Sequence)? _lastKey;
        private int _count;

        public int Record(string? contentId, ElementId questId, uint cfcId, byte sequence)
        {
            if (contentId == null)
            {
                Clear();
                throw new TaskException("Cannot retry a solo duty without a logged-in character.");
            }

            var key = (contentId, questId, cfcId, sequence);
            if (_lastKey != key)
            {
                _lastKey = key;
                _count = 0;
            }

            return ++_count;
        }

        public void Observe(string? contentId, Func<ElementId, byte?> getSequence)
        {
            if (_lastKey is not { } key)
                return;

            if (key.ContentId != contentId || getSequence(key.QuestId) != key.Sequence)
                Clear();
        }

        public void Clear()
        {
            _lastKey = null;
            _count = 0;
        }
    }

    internal sealed class Factory(BossModIpc bossModIpc, TerritoryData territoryData) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.SinglePlayerDuty ||
                !bossModIpc.CanRunQuestBattle(quest.Id, step.SinglePlayerDutyOptions))
                yield break;

            if (!territoryData.TryGetQuestBattleCfc(quest.Id, step.SinglePlayerDutyIndex, out var content))
                throw new TaskException("Failed to resolve the solo duty from quest data.");

            foreach (var task in CreateDutyTasks(content.Id, quest.Id, sequence.Sequence))
                yield return task;
        }

        internal static IEnumerable<ITask> CreateDutyTasks(uint cfcId, ElementId questId, byte sequence)
        {
            yield return new MountStep.UnmountTask();
            yield return new Start(cfcId);
            yield return new EnableAi(cfcId);
            yield return new WaitForExit(cfcId);
            yield return new DisableAi();
            yield return new WaitForOutcome(cfcId, questId, sequence);
        }
    }
    internal sealed record Start(uint CfcId) : ITask
    {
        public override string ToString() => $"Wait(BossMod, entered instance {CfcId})";
    }

    internal sealed class StartExecutor(ICondition condition) : TaskExecutor<Start>
    {
        private DateTime _enteredAt = DateTime.MinValue;

        protected override bool Start()
        {
            _enteredAt = DateTime.MinValue;
            return true;
        }

        public override unsafe ETaskResult Update()
        {
            GameMain* gameMain = GameMain.Instance();
            if (GenericHelpers.TryGetAddonMaster(out AddonMaster.ContentsFinderConfirm confirm) && confirm.IsAddonReady)
            {
                ContentsFinder* contentsFinder = ContentsFinder.Instance();
                if (contentsFinder != null &&
                    CanConfirmEntry(Task.CfcId, contentsFinder->QueueInfo.PoppedQueueEntry.Id,
                        contentsFinder->QueueInfo.PoppedQueueEntry.ContentType == ContentsType.Regular) &&
                    EzThrottler.Throttle("Questionable.SoloDuty.Confirm", 2000))
                    confirm.Commence();
            }
            if (gameMain->CurrentContentFinderConditionId != Task.CfcId)
                return ETaskResult.StillRunning;

            if (!condition[ConditionFlag.BoundByDuty])
                return ETaskResult.StillRunning;

            // Avoid starting the duty before the client is ready.
            if (_enteredAt == DateTime.MinValue)
                _enteredAt = DateTime.Now;

            return DateTime.Now - _enteredAt >= TimeSpan.FromSeconds(2)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;

        internal static bool CanConfirmEntry(uint requestedContent, uint poppedContent, bool regularDuty) =>
            requestedContent != 0 && regularDuty && poppedContent == requestedContent;
    }

    internal sealed record EnableAi(uint CfcId) : ITask
    {
        public override string ToString() => $"BossMod.RunSoloDuty({CfcId})";
    }

    internal sealed class EnableAiExecutor
    (
        BossModIpc bossModIpc) : TaskExecutor<EnableAi>, IStoppable
    {
        protected override bool Start()
        {
            bossModIpc.StartQuestBattle(Task.CfcId);
            return true;
        }

        public override ETaskResult Update()
        {
            bossModIpc.PollQuestBattle();
            return ETaskResult.TaskComplete;
        }

        public void StopNow() => bossModIpc.StopQuestBattle();

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record WaitForExit(uint CfcId) : ITask
    {
        public override string ToString() => $"Wait(BossMod, left instance {CfcId})";
    }

    internal sealed class WaitForExitExecutor
    (
        BossModIpc bossModIpc,
        MovementController movementController)
        : TaskExecutor<WaitForExit>, IStoppable, IDebugStateProvider
    {
        public string? GetDebugState()
        {
            if (!movementController.IsNavmeshReady)
                return $"Navmesh: {movementController.BuiltNavmeshPercent}%";

            return null;
        }

        public override unsafe ETaskResult Update()
        {
            bossModIpc.PollQuestBattle();
            return GameMain.Instance()->CurrentContentFinderConditionId != Task.CfcId
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public void StopNow() => bossModIpc.StopQuestBattle();

        public override bool ShouldInterruptOnDamage() => false;
        protected override bool Start() => true;
    }

    internal sealed record DisableAi : ITask
    {
        public override string ToString() => "BossMod.DisableAi";
    }

    internal sealed class DisableAiExecutor
    (
        BossModIpc bossModIpc) : TaskExecutor<DisableAi>, IStoppable
    {
        protected override bool Start()
        {
            bossModIpc.StopQuestBattle();
            return true;
        }

        public override ETaskResult Update()
        {
            bossModIpc.PollQuestBattle();
            return ETaskResult.TaskComplete;
        }

        public void StopNow() => bossModIpc.StopQuestBattle();

        public override bool ShouldInterruptOnDamage() => false;
    }

    // Retry if leaving a solo duty does not advance the quest within the grace period.
    internal sealed record WaitForOutcome(
        uint CfcId,
        ElementId QuestId,
        byte StartSequence) : ITask
    {
        public override string ToString() =>
            $"WaitForSinglePlayerDutyOutcome({CfcId}, seq={StartSequence})";
    }

    internal sealed class WaitForOutcomeExecutor(
        GameFunctions gameFunctions,
        ICondition condition,
        IChatGui chatGui,
        RetryCounter retryCounter,
        Configuration configuration,
        ILogger<WaitForOutcomeExecutor> logger,
        IQuestGameState gameState)
        : TaskExecutor<WaitForOutcome>, IDebugStateProvider
    {
        private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(15);
        private DateTime _readyAt = DateTime.MinValue;

        protected override bool Start() => true;

        public override unsafe ETaskResult Update()
        {
            // Quest progress cannot update while the player is busy.
            if (gameFunctions.IsOccupied() ||
                condition[ConditionFlag.WatchingCutscene] ||
                condition[ConditionFlag.WatchingCutscene78] ||
                condition[ConditionFlag.BetweenAreas] ||
                condition[ConditionFlag.BetweenAreas51])
            {
                _readyAt = DateTime.Now;
                return ETaskResult.StillRunning;
            }

            if (GameMain.Instance()->CurrentContentFinderConditionId == Task.CfcId)
            {
                _readyAt = DateTime.Now;
                return ETaskResult.StillRunning;
            }

            if (_readyAt == DateTime.MinValue)
                _readyAt = DateTime.Now;

            if (DateTime.Now - _readyAt < GracePeriod)
                return ETaskResult.StillRunning;

            // If the sequence advanced, the duty succeeded and UpdateCurrentQuest clears this task; keep waiting.
            QuestProgressInfo? progress = gameState.GetQuestProgressInfo(Task.QuestId);
            if (progress == null || progress.Sequence != Task.StartSequence)
                return ETaskResult.StillRunning;

            int failures = retryCounter.Record(ProfileController.CurrentContentId,
                Task.QuestId, Task.CfcId, Task.StartSequence);
            int limit = AttemptLimit(configuration.SinglePlayerDuties.RetryOnFailure,
                configuration.SinglePlayerDuties.MaxAttempts);
            if (failures >= limit)
            {
                logger.LogError(
                    "SinglePlayerDuty {Cfc} failed {Failures} times — stopping",
                    Task.CfcId, failures);
                chatGui.PrintError(
                    failures == 1
                        ? _L("Stopped: the quest battle failed. Try it manually, and report it if it keeps failing.")
                        : _LF("Stopped: the quest battle failed {0} times. Try it manually, and report it if it keeps failing.", failures),
                    CommandHandler.MessageTag, CommandHandler.TagColor);
                retryCounter.Clear();
                return ETaskResult.End;
            }

            logger.LogWarning(
                "SinglePlayerDuty {Cfc} appears to have failed (attempt {Failures}/{Max}, no quest progress {Seconds}s after leaving instance) — retrying step",
                Task.CfcId, failures, limit, GracePeriod.TotalSeconds);
            return ETaskResult.RetryStep;
        }

        public string? GetDebugState()
        {
            if (_readyAt == DateTime.MinValue)
                return "Waiting for player to be idle";
            TimeSpan remaining = GracePeriod - (DateTime.Now - _readyAt);
            return remaining > TimeSpan.Zero
                ? $"Outcome check in {remaining.TotalSeconds:F0}s"
                : "Outcome check pending";
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record LeaveParty : ITask
    {
        public override string ToString() => "LeaveParty()";
    }

    internal sealed class LeavePartyExecutor : TaskExecutor<LeaveParty>
    {
        protected override bool Start() => TryLeave();

        public override ETaskResult Update()
        {
            return TryLeave() ? ETaskResult.TaskComplete : ETaskResult.StillRunning;
        }

        public unsafe static bool TryLeave()
        {
            if (InfoProxyCrossRealm.IsLocalPlayerInParty())
            {
                if (InfoProxyCrossRealm.IsLocalPlayerPartyLeader())
                    return InfoProxyPartyMember.Instance()->DisbandParty();
                return InfoProxyPartyMember.Instance()->LeaveParty();
            }
            return true;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
