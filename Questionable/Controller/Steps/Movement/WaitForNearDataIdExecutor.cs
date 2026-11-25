namespace Questionable.Controller.Steps.Movement;

internal sealed class WaitForNearDataIdExecutor
(
    GameFunctions gameFunctions,
    IObjectTable objectTable) : TaskExecutor<WaitForNearDataId>
{
    protected override bool Start() => true;

    public override ETaskResult Update()
    {
        IGameObject? gameObject = gameFunctions.FindObjectByDataId(Task.DataId);
        if (gameObject == null ||
            (gameObject.Position - objectTable[0]!.Position).Length() > Task.StopDistance)
            throw new TaskException("Couldn't find the target nearby, and the step has no position to walk to.");

        return ETaskResult.TaskComplete;
    }

    public override bool ShouldInterruptOnDamage() => false;
}
