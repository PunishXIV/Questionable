namespace Questionable.Controller.Steps.Shared;

internal static class AetheryteShortcut
{
    internal sealed record Task(ElementId? ElementId, EAetheryteLocation TargetAetheryte, uint ExpectedTerritoryId)
        : ISkippableTask
    {
        public override string ToString() => $"UseAetheryte({TargetAetheryte})";
    }

    internal sealed class Executor
    (
        ILogger<Executor> logger,
        AetheryteFunctions aetheryteFunctions,
        GameFunctions gameFunctions,
        IClientState clientState,
        ICondition condition,
        QuestRegistry questRegistry) : TaskExecutor<Task>
    {
        private DateTime _continueAt;
        private bool _teleported;
        private bool _societyPause;

        internal bool HasTeleported => _teleported;

        protected override bool Start()
        {
            if (gameFunctions.IsMountOrSprintBlocked()) // Transporting
            {
                logger.LogInformation("Skipping aetheryte teleport, character is busy.");
                return false;
            }

            return true;
        }

        public override ETaskResult Update()
        {
            if (DateTime.Now < _continueAt)
                return ETaskResult.StillRunning;

            if (!_teleported)
            {
                _teleported = DoTeleport();
                return ETaskResult.StillRunning;
            }

            return clientState.TerritoryType == Task.ExpectedTerritoryId
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        private bool DoTeleport()
        {
            if (!aetheryteFunctions.CanTeleport(Task.TargetAetheryte))
            {
                if (!aetheryteFunctions.IsTeleportUnlocked())
                    throw new NeedsPlayerException("Teleport is not unlocked, attune to any aetheryte first.");

                _continueAt = DateTime.Now.AddSeconds(1);
                logger.LogTrace("Waiting for teleport cooldown...");
                return false;
            }

            _continueAt = DateTime.Now.AddSeconds(8);

            if (!aetheryteFunctions.IsAetheryteUnlocked(Task.TargetAetheryte))
            {
                throw new NeedsPlayerException("Aetheryte is not unlocked");
            }

            if (!_societyPause && Task.ElementId != null && questRegistry.TryGetQuest(Task.ElementId, out Quest? quest) && quest.Info.AlliedSociety != EAlliedSociety.None)
            {
                _societyPause = true;
                _continueAt = DateTime.Now.AddSeconds(0.5);
                logger.LogDebug("Waiting for soc teleport recalc cooldown...");
                return false;
            }
            ProgressContext =
                InteractionProgressContext.FromActionUseOrDefault(() =>
                    aetheryteFunctions.TeleportAetheryte(Task.TargetAetheryte));
            if (ProgressContext != null)
            {
                logger.LogInformation("Travelling via aetheryte...");
                return true;
            }

            throw new TaskException("Unable to teleport to aetheryte");
        }

        public override bool WasInterrupted() => condition[ConditionFlag.InCombat] || base.WasInterrupted();

        public override bool ShouldInterruptOnDamage() => true;
    }
}
