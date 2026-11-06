using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class NavmeshIpc : Ipc, IDisposable
{
    private readonly ILogger<NavmeshIpc> _logger;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("Nav.BuildProgress")] private readonly Func<float> _buildProgress = null!;
    [EzIPC("Nav.IsReady")] private readonly Func<bool> _isNavReady = null!;
    [EzIPC("Nav.PathfindCancelable")] private readonly Func<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>> _navPathfind = null!;
    [EzIPC("Path.IsRunning")] private readonly Func<bool> _pathIsRunning = null!;
    [EzIPC("Path.GetMovementAllowed")] private readonly Func<bool> _movementAllowed = null!;
    [EzIPC("SimpleMove.PathfindInProgress")] private readonly Func<bool> _simpleMoveInProgress = null!;
    [EzIPC("Nav.PathfindInProgress")] private readonly Func<bool> _pathfindInProgress = null!;
    [EzIPC("Nav.PathfindNumQueued")] private readonly Func<int> _queuedPathfinds = null!;
    [EzIPC("Path.ListWaypoints")] private readonly Func<List<Vector3>> _pathListWaypoints = null!;
    [EzIPC("Path.MoveTo")] private readonly Action<List<Vector3>, bool> _pathMoveTo = null!;
    [EzIPC("Path.SetTolerance")] private readonly Action<float> _pathSetTolerance = null!;
    [EzIPC("Path.Stop")] private readonly Action _pathStop = null!;
    [EzIPC("Query.Mesh.PointOnFloor")] private readonly Func<Vector3, bool, float, Vector3?> _queryPointOnFloor = null!;
    [EzIPC("Query.Mesh.NearestPointReachable")] private readonly Func<Vector3, float, float, Vector3?> _nearestPointReachable = null!;

    public NavmeshIpc(ILogger<NavmeshIpc> logger)
    {
        _logger = logger;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
    }

    public override string InternalName => "vnavmesh";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() => GetVersion() != null && _isNavReady(), fallback: false);

    public bool? GetPathRunning() => ReadOptional<bool>(() => _pathIsRunning());

    public bool IsPathRunning => GetPathRunning() != false;

    // Older vnavmesh builds lack these; a missing endpoint reads as unknown.
    public bool? GetMovementAllowed() => ReadOptional<bool>(() => _movementAllowed());
    public bool? GetSimpleMoveInProgress() => ReadOptional<bool>(() => _simpleMoveInProgress());
    public bool? GetPathfindInProgress() => ReadOptional<bool>(() => _pathfindInProgress());
    public int? GetQueuedPathfinds() => ReadOptional<int>(() => _queuedPathfinds());

    private static T? ReadOptional<T>(Func<T?> read) where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Stop()
    {
        if (GetVersion() == null)
            return;

        IpcInvoke.SafeAction(() => _pathStop(), _logger,
            "Could not stop navigating via navmesh {Version}", GetVersion());
    }

    public Task<List<Vector3>> Pathfind(Vector3 localPlayerPosition, Vector3 targetPosition, bool fly,
        CancellationToken cancellationToken)
    {
        try
        {
            _pathSetTolerance(0.25f);
            return _navPathfind(localPlayerPosition, targetPosition, fly, cancellationToken);
        }
        catch (Exception e)
        {
            return Task.FromException<List<Vector3>>(e);
        }
    }

    public void MoveTo(List<Vector3> position, bool fly)
    {
        _pathStop();
        _pathMoveTo(position, fly);
    }

    public Vector3? GetPointOnFloor(Lumina.Excel.Sheets.Level level) =>
        GetPointOnFloor(new Vector3(level.X, level.Y, level.Z), unlandable: false);

    public Vector3? GetPointOnFloor(Vector3 position, bool unlandable, float range = 0.2f) =>
        ReadOptional(() => _queryPointOnFloor(position, unlandable, range));

    public Vector3? GetNearestPointReachable(Vector3 position, float halfExtentXZ, float halfExtentY) =>
        ReadOptional(() => _nearestPointReachable(position, halfExtentXZ, halfExtentY));

    public List<Vector3> GetWaypoints()
    {
        if (GetPathRunning() != true)
            return [];
        return IpcInvoke.SafeFunc<List<Vector3>>(() => _pathListWaypoints(), []) ?? [];
    }

    public int GetBuildProgress() =>
        IpcInvoke.SafeFunc(() =>
        {
            float progress = _buildProgress();
            return progress < 0 ? 100 : (int)(progress * 100);
        }, 0);

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}
