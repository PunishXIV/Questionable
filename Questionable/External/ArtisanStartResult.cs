namespace Questionable.External;

internal enum ArtisanStartResult
{
    // These IPC actions return void. Submission is not proof of execution or completion.
    Submitted,
    MissingList,
    Busy,
    Unavailable,
    Failed,
}
