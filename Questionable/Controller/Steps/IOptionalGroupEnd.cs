namespace Questionable.Controller.Steps;

internal interface IOptionalGroupEnd : ITask
{
    bool RetryStep { get; }

    void OnFailed(Exception exception);
}
