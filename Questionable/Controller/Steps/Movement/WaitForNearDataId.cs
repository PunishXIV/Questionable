
namespace Questionable.Controller.Steps.Movement;

internal sealed record WaitForNearDataId(uint DataId, float StopDistance) : ITask
{
    public bool ShouldRedoOnInterrupt() => true;
}
