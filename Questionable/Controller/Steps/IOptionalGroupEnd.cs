namespace Questionable.Controller.Steps;

// The last task of an optional group. A task failing while this is still queued drops the rest of the group and
// plans the current step again, instead of stopping the run.
internal interface IOptionalGroupEnd : ITask
{
    // Plan the current step again after a failure; the group may have travelled away.
    bool RetryStep { get; }

    void OnFailed(Exception exception);
}
