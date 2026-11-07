using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class QuestAcceptanceController(Configuration configuration)
{
    // Waits queued for quests that weren't accepted yet. They end with the task plan.
    private readonly HashSet<ElementId> _queued = [];

    // The unaccepted next-quest selection. It survives replans but ends when this exact selection is replaced,
    // so selecting the same quest again starts a new expectation.
    private QuestController.QuestProgress? _selectedNext;

    public bool StopAfterNextAccept { get; set; }

    public void TrackQueued(ITask task, Func<ElementId, bool> isAccepted)
    {
        if (task is WaitAtEnd.QuestAccepted wait && !isAccepted(wait.ElementId))
            _queued.Add(wait.ElementId);
    }

    public void ClearQueued() => _queued.Clear();

    public void Select(QuestController.QuestProgress? selection, bool isAccepted) =>
        _selectedNext = isAccepted ? null : selection;

    public void ClearSelection() => _selectedNext = null;

    public void ObserveQueued(TaskQueue queue, Func<ElementId, bool> isAccepted, Action<ElementId> onAccepted)
    {
        foreach (ElementId questId in _queued.ToArray())
        {
            if (!IsQueued(queue, questId))
            {
                _queued.Remove(questId);
                continue;
            }

            // Fails for waits an earlier report's stop already ended.
            if (isAccepted(questId) && TryConsumeQueued(questId))
                onAccepted(questId);
        }
    }

    public bool TryConsumeQueued(ElementId questId)
    {
        if (!_queued.Remove(questId))
            return false;
        if (_selectedNext?.Quest.Id == questId)
            _selectedNext = null;
        return true;
    }

    public bool TryResolve(QuestController.QuestProgress next, bool isAccepted, bool isComplete, out bool newlyAccepted)
    {
        newlyAccepted = false;
        if (!isAccepted && (next.Quest.Info.IsRepeatable || !isComplete))
            return false;

        newlyAccepted = isAccepted && ReferenceEquals(_selectedNext, next);
        _selectedNext = null;
        if (newlyAccepted)
            _queued.Remove(next.Quest.Id);
        return true;
    }

    public bool ShouldStop(ElementId questId, bool isAccepted, bool isManual, out bool configStop)
    {
        configStop = false;
        if (isManual || !isAccepted)
            return false;

        configStop = configuration.Stop.Enabled &&
                     configuration.Stop.QuestsToStopWhenAccepted.Any(x => x == questId);
        return configStop || StopAfterNextAccept;
    }

    private static bool IsQueued(TaskQueue queue, ElementId questId) =>
        queue.CurrentTaskExecutor?.CurrentTask is WaitAtEnd.QuestAccepted current && current.ElementId == questId ||
        queue.RemainingTasks.OfType<WaitAtEnd.QuestAccepted>().Any(x => x.ElementId == questId);
}
