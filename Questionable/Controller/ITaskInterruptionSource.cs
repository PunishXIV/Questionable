namespace Questionable.Controller;

internal interface ITaskInterruptionSource
{
    event EventHandler? Interrupted;
}
