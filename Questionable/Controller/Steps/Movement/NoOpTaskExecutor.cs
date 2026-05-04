namespace Questionable.Controller.Steps.Movement;

internal sealed class NoOpTaskExecutor : TaskExecutor<NoOpTask>
{
    protected override bool Start() => true;

    public override ETaskResult Update() => ETaskResult.TaskComplete;

    public override bool ShouldInterruptOnDamage() => false;
}
