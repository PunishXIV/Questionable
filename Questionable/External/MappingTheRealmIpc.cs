using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class MappingTheRealmIpc : Ipc, IDisposable
{
    private readonly ILogger<MappingTheRealmIpc> _logger;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("ApiVersion")] private readonly Func<int> _apiVersion = null!;
    [EzIPC("IsPathing")] private readonly Func<bool> _isPathing = null!;
    [EzIPC("PathToTerritory")] private readonly Func<uint, Task> _pathToTerritory = null!;
    [EzIPC("PathTo")] private readonly Func<uint, Vector3, Task> _pathTo = null!;
    [EzIPC("PathToInteractable")] private readonly Func<uint, Vector3, uint, Task> _pathToInteractable = null!;
    [EzIPC("Stop")] private readonly Action _stop = null!;

    public MappingTheRealmIpc(ILogger<MappingTheRealmIpc> logger)
    {
        _logger = logger;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
    }

    public override string InternalName => "MappingTheRealm";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc<int?>(() => _apiVersion(), null) != null;

    // Any plugin's route, not only ours. 1.0.0.0 has no IsPathing yet, which reads as null.
    public bool? GetPathing() => IpcInvoke.SafeFunc<bool?>(() => _isPathing(), null);

    // The task ends when the player arrives, or faults with MappingTheRealm's reason.
    public Task Travel(uint territoryId, Vector3? position, uint? dataId)
    {
        try
        {
            return position is not { } at ? _pathToTerritory(territoryId)
                : dataId is { } id ? _pathToInteractable(territoryId, at, id)
                : _pathTo(territoryId, at);
        }
        catch (Exception e)
        {
            return Task.FromException(e);
        }
    }

    // Only cancels a route Questionable started.
    public void Stop() => IpcInvoke.SafeAction(() => _stop(), _logger, "Could not stop MappingTheRealm");

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}
