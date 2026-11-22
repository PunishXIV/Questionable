using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.ExcelServices;
using ECommons.UIHelpers.AtkReaderImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
namespace Questionable.Controller.Steps.Interactions;

internal static class Capture
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Capture)
                return null;
            if (step.DataId is not { } dataId || step.PetId is not { } petId || step.Position is not { } position)
                throw new ArgumentException("Capture needs DataId, PetId and Position", nameof(step));

            return new Task(dataId, petId, position);
        }
    }

    internal sealed record Task(uint DataId, uint PetId, Vector3 Position) : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;

        public override string ToString() => $"Capture({DataId} as pet {PetId})";
    }

    internal enum EStep
    {
        Check,
        Hunt,
        Fight,
        AfterFight,
    }

    internal sealed class Executor(
        GameFunctions gameFunctions,
        MovementController movementController,
        IObjectTable objectTable,
        ICondition condition,
        ILogger<Task> logger) : TaskExecutor<Task>
    {
        private const float WeakenedHp = 0.6f;
        private const float Reach = 3f;
        private const int MaxTries = 8;
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

        private EStep _step;
        private DateTime _nextTry;
        private DateTime _deadline;
        private uint _pagesTried;
        private ulong _targetId;
        private bool _captureUsed;
        private bool _checkAfter;
        private bool _eligible;
        private int _tries;
        private int _killedEarly;

        protected override unsafe bool Start()
        {
            if ((Job)PlayerState.Instance()->CurrentClassJobId != Job.BST)
                throw new TaskException("Switch to Beastmaster first.");

            _step = EStep.Check;
            _nextTry = DateTime.MinValue;
            _deadline = DateTime.Now + Timeout;
            _pagesTried = 0;
            _tries = 0;
            _killedEarly = 0;
            _checkAfter = false;
            return true;
        }

        public override ETaskResult Update()
        {
            if (DateTime.Now > _deadline)
                throw new TaskException($"Couldn't capture pet {Task.PetId} in 15 minutes; capture it yourself, then restart.");
            if (DateTime.Now < _nextTry || condition[ConditionFlag.Casting])
                return ETaskResult.StillRunning;

            _nextTry = DateTime.Now.AddSeconds(0.5);
            return _step switch
            {
                EStep.Check => CheckBestiary(),
                EStep.Hunt => Hunt(),
                EStep.Fight => Fight(),
                _ => AfterFight(),
            };
        }

        private unsafe ETaskResult CheckBestiary()
        {
            if (!Bestiary.TryGet(out var addon))
            {
                Bestiary.Open();
                _nextTry = DateTime.Now.AddSeconds(1);
                return ETaskResult.StillRunning;
            }

            ReaderXBMMonsterNotebook reader = new(addon);
            int slot = reader.CurrentPageEntries.FindIndex(x => x.Number == Task.PetId);
            if (slot < 0 && reader.PageCount > 0 && ++_pagesTried <= reader.PageCount)
            {
                Bestiary.NextPage(addon, reader);
                _nextTry = DateTime.Now.AddSeconds(1);
                return ETaskResult.StillRunning;
            }

            Bestiary.Close();
            _pagesTried = 0;
            if (slot >= 0 && reader.CurrentPageEntries[slot].Caught)
            {
                logger.LogInformation("Pet {Pet} is in the bestiary", Task.PetId);
                return ETaskResult.TaskComplete;
            }

            if (slot < 0)
                logger.LogWarning("Pet {Pet} isn't listed in the Master's Bestiary; treating it as not caught", Task.PetId);
            if (_tries >= MaxTries)
                throw new TaskException($"Capture failed {MaxTries} times for pet {Task.PetId}; capture it yourself, then restart.");

            _checkAfter = false;
            _step = EStep.Hunt;
            return ETaskResult.StillRunning;
        }

        private ETaskResult Hunt()
        {
            if (objectTable[0] is not { } player)
                return ETaskResult.StillRunning;

            IBattleNpc? target = objectTable.OfType<IBattleNpc>()
                .Where(x => x.BaseId == Task.DataId && x is { IsTargetable: true, IsDead: false, CurrentHp: > 0 } &&
                            (!x.StatusFlags.HasFlag(StatusFlags.InCombat) || x.TargetObjectId == player.GameObjectId))
                .MinBy(x => Vector3.Distance(x.Position, player.Position));
            if (target == null)
            {
                if (!movementController.IsPathRunning && Vector3.Distance(player.Position, Task.Position) > 10)
                    movementController.NavigateTo(EMovementType.Quest, null, Task.Position, new());
                return ETaskResult.StillRunning;
            }

            Engage(target, capture: true);
            return ETaskResult.StillRunning;
        }

        private unsafe void Engage(IBattleNpc target, bool capture)
        {
            _targetId = target.GameObjectId;
            _captureUsed = !capture;
            _eligible = capture && target.Level <= PlayerState.Instance()->CurrentLevel;
            logger.LogInformation("Fighting {Target} (Lv{Level}), capture: {Capture}", target.Name, target.Level, capture);
            _step = EStep.Fight;
        }

        private unsafe ETaskResult Fight()
        {
            if (objectTable[0] is not { } player)
                return ETaskResult.StillRunning;
            if (objectTable.SearchById(_targetId) is not IBattleNpc { IsDead: false, CurrentHp: > 0 } target)
            {
                if (_eligible && !_captureUsed)
                    _killedEarly++;
                _step = EStep.AfterFight;
                return ETaskResult.StillRunning;
            }

            if (player.Position.DistanceToXZ(target.Position) > Reach)
            {
                if (!movementController.IsPathRunning)
                    movementController.NavigateTo(EMovementType.Combat, target.BaseId, target.Position,
                        new() { StopDistance = Reach - 0.5f });
                return ETaskResult.StillRunning;
            }

            if (movementController.IsPathRunning)
                movementController.Stop();
            if (condition[ConditionFlag.Mounted])
            {
                gameFunctions.Unmount();
                _nextTry = DateTime.Now.AddSeconds(1);
                return ETaskResult.StillRunning;
            }

            float hp = (float)target.CurrentHp / target.MaxHp;
            float threshold = _killedEarly >= 2 ? 1f : WeakenedHp;
            if (ShouldCapture(_captureUsed, hp, threshold, target.Level, PlayerState.Instance()->CurrentLevel))
            {
                if (gameFunctions.UseAction(target, EAction.Capture))
                {
                    _captureUsed = true;
                    _checkAfter = true;
                    _tries++;
                    logger.LogInformation("Used Capture on {Target} at {Hp:P0} HP (try {Try})", target.Name, hp, _tries);
                }

                return ETaskResult.StillRunning;
            }

            gameFunctions.UseAction(target, EAction.SmashAxe);
            return ETaskResult.StillRunning;
        }

        private ETaskResult AfterFight()
        {
            if (objectTable[0] is not { } player)
                return ETaskResult.StillRunning;

            if (condition[ConditionFlag.InCombat])
            {
                if (objectTable.OfType<IBattleNpc>().FirstOrDefault(x =>
                        x is { IsDead: false, CurrentHp: > 0 } && x.TargetObjectId == player.GameObjectId) is { } attacker)
                    Engage(attacker, capture: attacker.BaseId == Task.DataId);
                return ETaskResult.StillRunning;
            }

            _step = _checkAfter ? EStep.Check : EStep.Hunt;
            _nextTry = DateTime.Now.AddSeconds(1);
            return ETaskResult.StillRunning;
        }

        internal static bool ShouldCapture(bool used, float hp, float threshold, byte targetLevel, short playerLevel) =>
            !used && targetLevel <= playerLevel && hp <= threshold;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
