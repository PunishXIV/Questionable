using Questionable.Model;
using Questionable.Model.Questing;
using System.Collections.Generic;

namespace Questionable.Controller.Steps;

internal abstract class SimpleTaskFactory : ITaskFactory
{
    public abstract ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step);

    public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
    {
        var task = CreateTask(quest, sequence, step);
        if (task != null)
            yield return task;
    }
}
