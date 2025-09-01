using Questionable.Model;
using Questionable.Model.Questing;
using System.Collections.Generic;

namespace Questionable.Controller.Steps;

internal interface ITaskFactory
{
    IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step);
}
