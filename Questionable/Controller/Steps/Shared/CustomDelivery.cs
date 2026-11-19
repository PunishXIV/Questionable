using static Questionable.Controller.Steps.ITaskExecutor;
namespace Questionable.Controller.Steps.Shared;

internal static class CustomDelivery
{
    internal sealed record Task(uint Npc, Configuration.EDeliveryItem Item) : ITask
    {
        public override string ToString() => $"CustomDelivery({Npc}, {Item})";
    }

    internal sealed class Executor(SatisfyIpc satisfy, ILogger<Executor> logger) : TaskExecutor<Task>, IStoppable
    {
        protected override bool Start()
        {
            if (satisfy.Start(Task.Npc, Task.Item))
            {
                logger.LogInformation("Satisfy is doing custom delivery NPC {Npc}", Task.Npc);
                return true;
            }

            logger.LogInformation("Satisfy skipped custom delivery NPC {Npc}", Task.Npc);
            return false;
        }

        public override ETaskResult Update() => satisfy.IsBusy ? ETaskResult.StillRunning : ETaskResult.TaskComplete;

        public void StopNow()
        {
            if (satisfy.IsBusy)
                satisfy.Stop();
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
