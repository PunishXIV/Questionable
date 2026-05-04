namespace Questionable.Controller.Steps.Movement;

internal sealed class LandTask : ITask
{
    public bool ShouldRedoOnInterrupt()
    {
        return true;
    }
    public override string ToString()
    {
        return "Land";
    }
}
