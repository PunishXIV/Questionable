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
    [EzIPC("PathToInteractableById")] private readonly Func<uint, uint, Task> _pathToInteractableById = null!;
    [EzIPC("PathToInteractableWithRange")] private readonly Func<uint, Vector3, uint, float, Task> _pathToInteractableWithRange = null!;
    [EzIPC("CanReach")] private readonly Func<uint, Vector3, CancellationToken, Task<bool>> _canReach = null!;
    [EzIPC("Stop")] private readonly Action _stop = null!;

    public MappingTheRealmIpc(ILogger<MappingTheRealmIpc> logger)
    {
        _logger = logger;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
    }

    public override string InternalName => "MappingTheRealm";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc<int?>(() => _apiVersion(), null) != null;

    // Any plugin's route, not only ours.
    public bool? GetPathing() => IpcInvoke.SafeFunc<bool?>(() => _isPathing(), null);

    // A range above 0 is yalms to the loaded object; 0 is the game's interaction range.
    public Task Travel(uint territoryId, Vector3? position, uint? dataId, float range = 0)
    {
        try
        {
            return (position, dataId) switch
            {
                ({ } at, { } id) when range > 0 => _pathToInteractableWithRange(territoryId, at, id, range),
                ({ } at, { } id) => _pathToInteractable(territoryId, at, id),
                ({ } at, _) => _pathTo(territoryId, at),
                (_, { } id) => _pathToInteractableById(territoryId, id),
                _ => _pathToTerritory(territoryId),
            };
        }
        catch (Exception e)
        {
            return Task.FromException(e);
        }
    }

    public Task<bool> CanReach(uint territoryId, Vector3 position, CancellationToken token) =>
        _canReach(territoryId, position, token);

    // Only cancels a route Questionable started.
    public void Stop() => IpcInvoke.SafeAction(() => _stop(), _logger, "Could not stop MappingTheRealm");

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}
