using ECommons.UIHelpers.AtkReaderImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Callback = ECommons.Automation.Callback;
namespace Questionable.Controller.Steps.Interactions;

// Event lookup follows AutoDuty's Crucible code (CrucibleUi).
internal static class AssignBattlehorn
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.AssignBattlehorn)
                return null;
            if (step.PetId is not { } petId || step.Battlehorn is not { } slot || slot is < 1 or > 3)
                throw new ArgumentException("AssignBattlehorn needs PetId and Battlehorn 1-3");

            return new Task(petId, slot);
        }
    }

    internal sealed record Task(uint PetId, byte Battlehorn) : ITask
    {
        public bool ShouldRedoOnInterrupt() => true;

        public override string ToString() => $"AssignBattlehorn({PetId} -> {Battlehorn})";
    }

    internal enum EStep
    {
        Open,
        Find,
        Menu,
        Wait,
    }

    internal sealed class Executor(ILogger<Task> logger) : TaskExecutor<Task>
    {
        private const uint FirstEntryParam = 4;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private EStep _step;
        private DateTime _nextTry;
        private DateTime _deadline;
        private uint _pagesTried;

        protected override bool Start()
        {
            if (IsAssigned())
            {
                logger.LogInformation("Pet {Pet} is already on battlehorn {Slot}", Task.PetId, Task.Battlehorn);
                return false;
            }

            _step = EStep.Open;
            _nextTry = DateTime.MinValue;
            _deadline = DateTime.Now + Timeout;
            _pagesTried = 0;
            return true;
        }

        public override unsafe ETaskResult Update()
        {
            if (IsAssigned())
            {
                Bestiary.Close();
                return ETaskResult.TaskComplete;
            }

            if (DateTime.Now > _deadline)
                throw new TaskException(
                    $"Couldn't put pet {Task.PetId} on battlehorn {Task.Battlehorn}; assign it in the Master's Bestiary, then restart.");
            if (DateTime.Now < _nextTry)
                return ETaskResult.StillRunning;
            _nextTry = DateTime.Now.AddSeconds(1);

            if (!Bestiary.TryGet(out var bestiary))
            {
                logger.LogInformation("Opening the Master's Bestiary");
                Bestiary.Open();
                return ETaskResult.StillRunning;
            }

            switch (_step)
            {
                case EStep.Open or EStep.Find:
                    ReaderXBMMonsterNotebook reader = new(bestiary);
                    int slot = reader.CurrentPageEntries.FindIndex(x => x.Number == Task.PetId);
                    if (slot < 0)
                    {
                        if (reader.PageCount == 0 || ++_pagesTried > reader.PageCount)
                            throw new TaskException(
                                $"Pet {Task.PetId} isn't in the Master's Bestiary (clear its filter if one is set).");
                        Bestiary.NextPage(bestiary, reader);
                        _step = EStep.Find;
                        return ETaskResult.StillRunning;
                    }

                    logger.LogInformation("Right-clicking pet {Pet} (slot {Slot})", Task.PetId, slot);
                    Callback.Fire(bestiary, true, 5, slot);
                    RightClick(bestiary, FirstEntryParam + (uint)slot);
                    _step = EStep.Menu;
                    return ETaskResult.StillRunning;

                case EStep.Menu:
                    if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>("ContextMenu", out var menu) ||
                        !GenericHelpers.IsAddonReady(menu))
                    {
                        _step = EStep.Find;
                        return ETaskResult.StillRunning;
                    }

                    logger.LogInformation("Choosing battlehorn {Slot}", Task.Battlehorn);
                    Callback.Fire(menu, true, 0, Task.Battlehorn - 1, 0);
                    _step = EStep.Wait;
                    return ETaskResult.StillRunning;

                default:
                    _step = EStep.Find;
                    return ETaskResult.StillRunning;
            }
        }

        private unsafe bool IsAssigned() => ActionManager.Instance()->BeastmasterPets[Task.Battlehorn - 1] == Task.PetId;

        private unsafe void RightClick(AtkUnitBase* addon, uint param)
        {
            AtkEvent* evt = FindEvent(&addon->UldManager, AtkEventType.MouseDown, param, 0);
            if (evt == null)
            {
                logger.LogWarning("No mouse event {Param} in the Master's Bestiary", param);
                return;
            }

            AtkEventData data = new();
            data.MouseData.ButtonId = 1;
            addon->ReceiveEvent(AtkEventType.MouseDown, (int)param, evt, &data);
        }

        private static unsafe AtkEvent* FindEvent(AtkUldManager* uld, AtkEventType type, uint param, int depth)
        {
            if (depth > 6)
                return null;

            for (int i = 0; i < uld->NodeListCount; i++)
            {
                AtkResNode* node = uld->NodeList[i];
                if (node == null || !node->IsVisible())
                    continue;

                for (AtkEvent* evt = node->AtkEventManager.Event; evt != null; evt = evt->NextEvent)
                    if (evt->State.EventType == type && evt->Param == param)
                        return evt;

                AtkComponentNode* component = node->GetAsAtkComponentNode();
                if (component == null || component->Component == null)
                    continue;

                AtkEvent* found = FindEvent(&component->Component->UldManager, type, param, depth + 1);
                if (found != null)
                    return found;
            }

            return null;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
