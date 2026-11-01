namespace Questionable.External;

internal enum ExternalOperationStatus
{
    Unknown = 0,
    Running = 1,
    CancellationRequested = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}
