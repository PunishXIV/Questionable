using static Questionable.Controller.Steps.ITaskExecutor;
namespace Questionable.Controller.Steps.Common;

internal sealed class WaitLifestream
{
    internal sealed record Task : ITask
    {
        public override string ToString() => "Wait(Lifestream)";
    }

    internal sealed class Executor(LifestreamIpc lifestreamIpc) : TaskExecutor<Task>, IDebugStateProvider
    {
        public override ETaskResult Update() => !lifestreamIpc.IsBusy ? ETaskResult.TaskComplete : ETaskResult.StillRunning;

        public override bool ShouldInterruptOnDamage() => false;

        public string? GetDebugState()
        {
            if (lifestreamIpc.IsBusy)
                return "Lifestream: busy";

            return null;
        }
        protected override bool Start() => true;
    }
}
