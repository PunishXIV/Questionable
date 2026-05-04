using System.Collections.Generic;
using Questionable.Model;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps;

internal abstract class SimpleTaskFactory : ITaskFactory
{
    public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
    {
        ITask? task = CreateTask(quest, sequence, step);
        if (task != null)
            yield return task;
    }
    public abstract ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step);
}
