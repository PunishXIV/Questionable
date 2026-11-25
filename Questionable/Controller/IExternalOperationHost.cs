namespace Questionable.Controller;

internal interface IExternalOperationHost
{
    ITaskExecutor? CurrentExecutor { get; }
    bool IsActive { get; }
    string? Status { set; }
    void Stop(string reason);
}
