namespace Questionable.External;

internal enum GatherStartResult
{
    Uncertain = -1,
    Started = 0,
    Busy = 1,
    Unsupported = 2,
    Invalid = 3,
    NotReady = 4,
}
