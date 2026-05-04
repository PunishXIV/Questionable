using Questionable.External;
namespace Questionable.Controller.Steps.Common;

internal sealed class WaitLifestream
{
    internal sealed record Task : ITask
    {
        public override string ToString()
        {
            return "Wait(Lifestream)";
        }
    }

    internal sealed class Executor(LifestreamIpc lifestreamIpc) : TaskExecutor<Task>, IDebugStateProvider
    {
        public override ETaskResult Update()
        {
            return !lifestreamIpc.IsBusy ? ETaskResult.TaskComplete : ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage()
        {
            return false;
        }

        public string? GetDebugState()
        {
            if (lifestreamIpc.IsBusy)
                return "Lifestream: busy";
            else
                return null;
        }
        protected override bool Start()
        {
            return true;
        }
    }
}
