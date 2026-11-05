namespace Questionable.Controller;

/// <summary>The scheduling state needed to reconcile providers without owning quest decisions.</summary>
internal interface IExternalOperationHost
{
    ITaskExecutor? CurrentExecutor { get; }
    bool IsActive { get; }
    string? Status { set; }
    void Stop(string reason);
}
