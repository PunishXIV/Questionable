namespace Questionable.Controller.Steps.Movement;

internal sealed class LandTask : ITask
{
    public bool ShouldRedoOnInterrupt() => true;
    public override string ToString() => "Land";
}
