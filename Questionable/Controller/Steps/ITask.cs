namespace Questionable.Controller.Steps;

internal interface ITask
{
    bool ShouldRedoOnInterrupt() => false;
}
