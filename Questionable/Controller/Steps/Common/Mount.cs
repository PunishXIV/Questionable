using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Common.Math;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using Questionable.Functions;

namespace Questionable.Controller.Steps.Common;

internal static class Mount
{
    internal sealed record MountTask(
        ushort TerritoryId,
        EMountIf MountIf,
        Vector3? Position = null) : ITask
    {
        public Vector3? Position { get; } = MountIf == EMountIf.AwayFromPosition
            ? Position ?? throw new ArgumentNullException(nameof(Position))
            : null;

        public bool ShouldRedoOnInterrupt() => true;

        public override string ToString() => "Mount";
    }

    internal sealed class MountEvaluator(
        GameFunctions gameFunctions,
        ICondition condition,
        TerritoryData territoryData,
        IClientState clientState,
        ILogger<MountEvaluator> logger)
    {
        public unsafe MountResult EvaluateMountState(MountTask task, bool dryRun, ref DateTime retryAt)
        {
            if (condition[ConditionFlag.Mounted])
                return MountResult.DontMount;

            LogLevel logLevel = dryRun ? LogLevel.None : LogLevel.Information;

            if (!territoryData.CanUseMount(task.TerritoryId))
            {
                logger.Log(logLevel, "Can't use mount in current territory {Id}", task.TerritoryId);
                return MountResult.DontMount;
            }

            if (gameFunctions.HasStatusPreventingMount())
            {
                logger.Log(logLevel, "Can't mount due to status preventing sprint or mount");
                return MountResult.DontMount;
            }

            if (task.MountIf == EMountIf.AwayFromPosition)
            {
                Vector3 playerPosition = clientState.LocalPlayer?.Position ?? Vector3.Zero;
                float distance = System.Numerics.Vector3.Distance(playerPosition, task.Position.GetValueOrDefault());
                if (task.TerritoryId == clientState.TerritoryType && distance < 30f && !Conditions.Instance()->Diving)
                {
                    logger.Log(logLevel, "Not using mount, as we're close to the target");
                    return MountResult.DontMount;
                }

                logger.Log(logLevel,
                    "Want to use mount if away from destination ({Distance} yalms), trying (in territory {Id})...",
                    distance, task.TerritoryId);
            }
            else
                logger.Log(logLevel, "Want to use mount, trying (in territory {Id})...", task.TerritoryId);

            if (!condition[ConditionFlag.InCombat])
            {
                if (dryRun)
                    retryAt = DateTime.Now.AddSeconds(0.5);
                return MountResult.Mount;
            }
            else
                return MountResult.WhenOutOfCombat;
        }
    }

    internal sealed class MountExecutor(
        GameFunctions gameFunctions,
        ICondition condition,
        MountEvaluator mountEvaluator,
        ILogger<MountExecutor> logger) : TaskExecutor<MountTask>
    {
        private bool _mountTriggered;
        private DateTime _retryAt = DateTime.MinValue;

        protected override bool Start()
        {
            _mountTriggered = false;
            return mountEvaluator.EvaluateMountState(Task, false, ref _retryAt) == MountResult.Mount;
        }

        public override ETaskResult Update()
        {
            if (_mountTriggered && !condition[ConditionFlag.Mounted] && DateTime.Now > _retryAt)
            {
                logger.LogInformation("Not mounted, retrying...");
                _mountTriggered = false;
                _retryAt = DateTime.MaxValue;
            }

            if (!_mountTriggered)
            {
                if (gameFunctions.HasStatusPreventingMount())
                {
                    logger.LogInformation("Can't mount due to status preventing sprint or mount");
                    return ETaskResult.TaskComplete;
                }

                ProgressContext =
                    InteractionProgressContext.FromActionUse(() => _mountTriggered = gameFunctions.Mount());

                _retryAt = DateTime.Now.AddSeconds(5);
                return ETaskResult.StillRunning;
            }

            return condition[ConditionFlag.Mounted]
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal enum MountResult
    {
        DontMount,
        Mount,
        WhenOutOfCombat,
    }

    internal sealed record UnmountTask : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;

        public override string ToString() => "Unmount";
    }

    internal sealed class UnmountExecutor(
        ICondition condition,
        ILogger<UnmountTask> logger,
        GameFunctions gameFunctions,
        IClientState clientState)
        : TaskExecutor<UnmountTask>
    {
        private bool _unmountTriggered;
        private DateTime _continueAt = DateTime.MinValue;

        protected override bool Start()
        {
            if (!condition[ConditionFlag.Mounted])
                return false;

            logger.LogInformation("Step explicitly wants no mount, trying to unmount...");
            if (condition[ConditionFlag.InFlight])
            {
                gameFunctions.Unmount();
                _continueAt = DateTime.Now.AddSeconds(1);
                return true;
            }

            _unmountTriggered = gameFunctions.Unmount();
            _continueAt = DateTime.Now.AddSeconds(1);
            return true;
        }

        public override ETaskResult Update()
        {
            if (_continueAt >= DateTime.Now)
                return ETaskResult.StillRunning;

            if (IsUnmounting())
                return ETaskResult.StillRunning;

            if (!_unmountTriggered)
            {
                // if still flying, we still need to land
                if (condition[ConditionFlag.InFlight])
                    gameFunctions.Unmount();
                else
                    _unmountTriggered = gameFunctions.Unmount();

                _continueAt = DateTime.Now.AddSeconds(1);
                return ETaskResult.StillRunning;
            }

            if (condition[ConditionFlag.Mounted] && condition[ConditionFlag.InCombat])
            {
                _unmountTriggered = gameFunctions.Unmount();
                _continueAt = DateTime.Now.AddSeconds(1);
                return ETaskResult.StillRunning;
            }

            return condition[ConditionFlag.Mounted]
                ? ETaskResult.StillRunning
                : ETaskResult.TaskComplete;
        }

        private unsafe bool IsUnmounting()
        {
            IPlayerCharacter? localPlayer = clientState.LocalPlayer;
            if (localPlayer != null)
            {
                BattleChara* battleChara = (BattleChara*) localPlayer.Address;
                return (battleChara->Mount.Flags & 1) == 1;
            }

            return false;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    public enum EMountIf
    {
        Always,
        AwayFromPosition,
    }
}
