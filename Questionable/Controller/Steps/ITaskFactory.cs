using System.Collections.Generic;
using Questionable.Domain;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps;

internal interface ITaskFactory
{
    IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step);
}
