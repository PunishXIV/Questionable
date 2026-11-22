using System.Collections.ObjectModel;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using AozAction = Lumina.Excel.Sheets.AozAction;
namespace Questionable.Controller.Steps.Interactions;

internal static class ActionStep
{
    internal sealed class Factory : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Action)
                return [];
            if (!step.Action.HasValue)
                throw new ArgumentException($"{nameof(step.Action)} is missing", nameof(step));

            ITask task = OnObject(step.DataId, quest, step.Action.Value, step.CompletionQuestVariablesFlags);
            if (step.Action.Value.RequiresMount())
                return [task];

            return [new MountStep.UnmountTask(), task];
        }

        public static ITask OnObject(uint? dataId, Quest quest, EAction action, List<QuestWorkValue?>? completionFlags)
        {
            if (action is EAction.FumaShuriken or EAction.Katon or EAction.Raiton)
            {
                if (!dataId.HasValue)
                    throw new ArgumentNullException(nameof(dataId));

                return new UseMudraOnObject(dataId.Value, action);
            }

            return new UseOnObject(dataId, quest, action, completionFlags);
        }
    }

    internal sealed record UseOnObject
    (
        uint? DataId,
        Quest? Quest,
        EAction Action,
        List<QuestWorkValue?>? CompletionFlags,
        bool IgnoreResult = false,
        float Timeout = 0.5f) : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;
        public override string ToString() => $"Action({Action})";
    }

    internal sealed class UseOnObjectExecutor
    (
        GameFunctions gameFunctions,
        InterruptHandler interruptHandler,
        ICondition condition,
        ILogger<UseOnObject> logger,
        IQuestGameState gameState,
        IDataManager dataManager) : TaskExecutor<UseOnObject>
    {
        private const int MaxAttempts = 10;
        private const int BlueMageSlots = 24;

        // The game accepting an action doesn't mean it went off, so give the server time to confirm before retrying.
        private static readonly TimeSpan AcceptedRetryDelay = TimeSpan.FromSeconds(3);

        private DateTime _continueAt = DateTime.MinValue;
        private DateTime _retryAt = DateTime.MinValue;
        private int _attempts;
        private bool _accepted;
        private readonly List<(InterruptHandler.ActionRequest Request, long AfterVersion)> _requests = [];
        private bool _confirmed;

        protected override bool Start()
        {
            _requests.Clear();
            _confirmed = false;
            _attempts = 0;
            _accepted = false;
            _continueAt = DateTime.MinValue;
            _retryAt = DateTime.MinValue;
            if (SetBlueMageSpell())
            {
                // The spell can't be used until the server has the new action list.
                _continueAt = DateTime.Now.AddSeconds(1);
                return true;
            }

            if (Task.DataId != null)
            {
                IGameObject? gameObject = gameFunctions.FindObjectByDataId(Task.DataId.Value);
                if (gameObject == null)
                {
                    logger.LogWarning("No game object with dataId {DataId}", Task.DataId);
                    return false;
                }

                if (gameObject.IsTargetable)
                {
                    if (Task.Action == EAction.Diagnosis)
                    {
                        // Eukrasia changes Diagnosis into a different action.
                        if (gameFunctions.HasStatus(EStatus.Eukrasia))
                        {
                            if (GameFunctions.RemoveStatus(EStatus.Eukrasia))
                            {
                                // Wait 2 seconds, otherwise it uses Eukrasian Diagnosis.
                                _continueAt = DateTime.Now.AddSeconds(2);
                                return true;
                            }
                        }
                    }

                    TryUseAction(gameObject);
                    _continueAt = DateTime.Now.AddSeconds(Task.Timeout);
                    return true;
                }
            }
            else
            {
                TryUseAction(null);
                _continueAt = DateTime.Now.AddSeconds(Task.Timeout);
                return true;
            }

            return true;
        }

        public override ETaskResult Update() => Update(CheckCompletionFlags);

        internal ETaskResult Update(Func<ETaskResult> checkCompletionFlags)
        {
            if (DateTime.Now <= _continueAt)
                return ETaskResult.StillRunning;
            if (Task.IgnoreResult)
                return ETaskResult.TaskComplete;

            _confirmed |= _requests.Any(request => interruptHandler.Confirmations.Contains(request.Request, request.AfterVersion));
            if (_confirmed)
                return checkCompletionFlags();

            // if the quest already progressed, don't use the action again even if we missed the confirmation
            if (HasCompletionFlags() && checkCompletionFlags() == ETaskResult.TaskComplete)
                return ETaskResult.TaskComplete;

            if (_attempts >= MaxAttempts)
            {
                if (!_accepted)
                    throw new TaskException($"Unable to use action {Task.Action}");

                // Some actions never send an action effect.
                logger.LogWarning("No confirmation for action {Action} after {Attempts} attempts, assuming it was used",
                    Task.Action, _attempts);
                return checkCompletionFlags();
            }

            if (DateTime.Now < _retryAt || condition[ConditionFlag.Casting])
                return ETaskResult.StillRunning;

            if (Task.DataId != null)
            {
                IGameObject? gameObject = gameFunctions.FindObjectByDataId(Task.DataId.Value);
                if (gameObject == null || !gameObject.IsTargetable)
                    return ETaskResult.StillRunning;

                TryUseAction(gameObject);
            }
            else
                TryUseAction(null);

            return ETaskResult.StillRunning;
        }

        // Based on WrathCombo's BlueMageService.
        private unsafe bool SetBlueMageSpell()
        {
            uint actionId = (uint)Task.Action & 0xFFFF;
            if ((Job)PlayerState.Instance()->CurrentClassJobId != Job.BLU ||
                !dataManager.GetExcelSheet<AozAction>().Any(x => x.Action.RowId == actionId))
                return false;

            ActionManager* actionManager = ActionManager.Instance();
            uint[] slots = [.. Enumerable.Range(0, BlueMageSlots).Select(i => actionManager->GetActiveBlueMageActionInSlot(i))];
            int? slot = BlueMageSlot(slots, actionId);
            if (slot == null)
                return false;
            if (slot < 0)
                throw new TaskException($"Your Blue Mage action list is full; free a slot for {Task.Action}.");

            logger.LogInformation("Putting {Action} in Blue Mage slot {Slot}", Task.Action, slot.Value + 1);
            actionManager->AssignBlueMageActionToSlot(slot.Value, actionId);
            return true;
        }

        internal static int? BlueMageSlot(IReadOnlyList<uint> slots, uint actionId) =>
            slots.Contains(actionId) ? null : slots.ToList().IndexOf(0);

        private void TryUseAction(IGameObject? gameObject)
        {
            long afterVersion = interruptHandler.Confirmations.Version;
            InterruptHandler.ActionRequest? request;
            bool accepted = gameObject != null
                ? gameFunctions.UseAction(gameObject, Task.Action, out request)
                : gameFunctions.UseAction(Task.Action, out request);

            RecordAttempt(accepted, request, afterVersion);
        }

        internal void RecordAttempt(bool accepted, InterruptHandler.ActionRequest? request, long afterVersion)
        {
            // Hooks can queue a native request while returning false. Its matching spell/target still proves execution.
            if (request != null)
                _requests.Add((accepted ? request : request with { SourceSequence = null }, afterVersion));
            _attempts++;
            _accepted |= accepted;
            _retryAt = DateTime.Now + (accepted ? AcceptedRetryDelay : TimeSpan.FromSeconds(Task.Timeout));
            logger.LogDebug("Action {Action} attempt {Attempt}, accepted: {Accepted}", Task.Action, _attempts,
                accepted);
        }

        private bool HasCompletionFlags() =>
            Task.Quest != null &&
            Task.CompletionFlags != null &&
            QuestWorkUtils.HasCompletionFlags(Task.CompletionFlags);

        private ETaskResult CheckCompletionFlags()
        {
            if (!HasCompletionFlags())
                return ETaskResult.TaskComplete;

            QuestProgressInfo? questWork = gameState.GetQuestProgressInfo(Task.Quest!.Id);
            return questWork != null &&
                   QuestWorkUtils.Matches(Task.CompletionFlags!, questWork)
                ? ETaskResult.TaskComplete
                : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }

    internal sealed record UseMudraOnObject
    (
        uint DataId,
        EAction Action)
        : ITask
    {
        public override string ToString() => $"Mudra({Action})";
    }

    internal sealed class UseMudraOnObjectExecutor
    (
        GameFunctions gameFunctions,
        ILogger<UseMudraOnObject> logger) : TaskExecutor<UseMudraOnObject>
    {
        private static readonly ReadOnlyDictionary<EAction, Dictionary<EAction, EAction>> Combos =
            new Dictionary<EAction, Dictionary<EAction, EAction>>
            {
                { EAction.FumaShuriken, new() { { EAction.Ninjutsu, EAction.Ten } } },
                { EAction.Raiton, new() { { EAction.Ninjutsu, EAction.Ten }, { EAction.FumaShuriken, EAction.Chi } } },
                { EAction.Katon, new() { { EAction.Ninjutsu, EAction.Chi }, { EAction.FumaShuriken, EAction.Ten } } }
            }.AsReadOnly();

        private DateTime _continueAt = DateTime.MinValue;

        protected override bool Start() => true;

        public override unsafe ETaskResult Update()
        {
            if (DateTime.Now < _continueAt)
                return ETaskResult.StillRunning;

            EAction adjustedNinjutsuId = (EAction)ActionManager.Instance()->GetAdjustedActionId((uint)EAction.Ninjutsu);
            if (adjustedNinjutsuId == EAction.RabbitMedium)
            {
                _continueAt = DateTime.Now.AddSeconds(1);
                return ETaskResult.StillRunning;
            }

            IGameObject? gameObject = gameFunctions.FindObjectByDataId(Task.DataId);
            if (gameObject == null || !gameObject.IsTargetable)
                return ETaskResult.StillRunning;

            if (adjustedNinjutsuId == Task.Action)
            {
                _continueAt = DateTime.Now.AddSeconds(0.25);
                return gameFunctions.UseAction(gameObject, Task.Action)
                    ? ETaskResult.TaskComplete
                    : ETaskResult.StillRunning;
            }

            if (Combos.TryGetValue(Task.Action, out Dictionary<EAction, EAction>? combo))
            {
                if (combo.TryGetValue(adjustedNinjutsuId, out EAction mudra))
                {
                    _continueAt = DateTime.Now.AddSeconds(0.25);
                    gameFunctions.UseAction(mudra);
                    return ETaskResult.StillRunning;
                }

                _continueAt = DateTime.Now.AddSeconds(0.25);
                return ETaskResult.StillRunning;
            }

            logger.LogError("Unable to find relevant combo for {Action}", Task.Action);
            return ETaskResult.TaskComplete;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
