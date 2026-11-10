namespace Questionable.Controller.Steps;

internal interface ITaskFactory
{
    IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step);
}
