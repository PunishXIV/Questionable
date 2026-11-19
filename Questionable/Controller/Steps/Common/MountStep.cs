using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
namespace Questionable.Controller.Steps.Common;

internal static class MountStep
{
    internal sealed record UnmountTask : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;

        public override string ToString() => "Unmount";
    }

    internal sealed class UnmountExecutor
    (
        ICondition condition,
        ILogger<UnmountTask> logger,
        GameFunctions gameFunctions,
        IObjectTable objectTable)
        : TaskExecutor<UnmountTask>
    {
        private DateTime _continueAt = DateTime.MinValue;
        private bool _unmountTriggered;

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
            IPlayerCharacter? localPlayer = (IPlayerCharacter?)objectTable[0];
            if (localPlayer != null)
            {
                BattleChara* battleChara = (BattleChara*)localPlayer.Address;
                return (battleChara->Mount.Flags & 1) == 1;
            }

            return false;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
