namespace Questionable.Controller.Steps.Movement;

internal sealed class NoOpTaskExecutor : TaskExecutor<NoOpTask>
{
    protected override bool Start()
    {
        return true;
    }

    public override ETaskResult Update()
    {
        return ETaskResult.TaskComplete;
    }

    public override bool ShouldInterruptOnDamage()
    {
        return false;
    }
}
