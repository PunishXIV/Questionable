namespace Questionable.Controller;

/// <summary>The job state from Feesh's <c>Catching</c> text.</summary>
internal enum EFeeshJobState
{
    /// <summary>Not readable.</summary>
    Unavailable,
    /// <summary>Empty: no job has run yet.</summary>
    None,
    Fishing,
    Finishing,
    Done,
    Partial,
    Failed,
    Cancelled,
    Stopped,
    /// <summary>Unknown wording; never treated as finished.</summary>
    Unrecognized,
}
