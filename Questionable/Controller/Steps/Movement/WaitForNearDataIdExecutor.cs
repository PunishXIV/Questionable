using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Questionable.Functions;
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
        {
            throw new TaskException("Object not found or too far away, no position so we can't move");
        }

        return ETaskResult.TaskComplete;
    }

    public override bool ShouldInterruptOnDamage() => false;
}
