using Questionable.Controller.Steps.Shared;
using Quest = Questionable.Domain.Quest;
namespace Questionable.Controller.Steps.Common;

internal static class Ventures
{
    internal sealed class Factory(
        RetainerPass pass,
        Configuration configuration,
        IClientState clientState,
        IQuestGameState gameState,
        QuestFunctions questFunctions,
        GameFunctions gameFunctions,
        ICondition condition,
        QuestPriorityManager priorityManager) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (configuration.General.RetainersAtBell)
            {
                bool start = pass.StartPending;
                pass.StartPending = false;
                if (SummoningBell.Waiting || !start &&
                    (!BetweenQuests.CanDetour(quest, step, questFunctions, condition, gameFunctions, priorityManager) ||
                     !pass.IsDue()))
                    return [];

                return SummoningBell.Tasks(configuration.General.RetainerBell, clientState.TerritoryType, gameState.GrandCompany);
            }

            if (!BetweenQuests.CanDetour(quest, step, questFunctions, condition, gameFunctions, priorityManager) ||
                !pass.IsDue())
                return [];

            return [new Task()];
        }
    }

    internal sealed record Task : ITask
    {
        public override string ToString() => "RetainerPass";
    }

    // RetainerPass stops questing on the next frame and starts it again after the pass.
    internal sealed class Executor(RetainerPass pass) : TaskExecutor<Task>
    {
        protected override bool Start() => pass.Begin();
        public override ETaskResult Update() => ETaskResult.StillRunning;
        public override bool ShouldInterruptOnDamage() => false;
    }
}
