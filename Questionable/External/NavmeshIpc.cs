using Dalamud.Plugin.Ipc;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class NavmeshIpc(IDalamudPluginInterface pluginInterface, ILogger<NavmeshIpc> logger) : Ipc
{
    public override string InternalName => "vnavmesh";
    public override Version? GetVersion() => pluginInterface.InstalledPlugins
        .FirstOrDefault(x => x.InternalName == InternalName && x.IsLoaded)?.Version;
    public override bool IsReady() => IpcInvoke.SafeFunc(() => GetVersion() != null && _isNavReady.InvokeFunc(), fallback: false);
    private readonly ICallGateSubscriber<float> _buildProgress = pluginInterface.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
    private readonly ICallGateSubscriber<bool> _isNavReady = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
    private readonly ICallGateSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>> _navPathfind =
        pluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>(
            "vnavmesh.Nav.PathfindCancelable");
    private readonly ICallGateSubscriber<bool> _pathIsRunning = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
    private readonly ICallGateSubscriber<bool> _movementAllowed = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.GetMovementAllowed");
    private readonly ICallGateSubscriber<bool> _simpleMoveInProgress = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
    private readonly ICallGateSubscriber<bool> _pathfindInProgress = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
    private readonly ICallGateSubscriber<int> _queuedPathfinds = pluginInterface.GetIpcSubscriber<int>("vnavmesh.Nav.PathfindNumQueued");
    private readonly ICallGateSubscriber<List<Vector3>> _pathListWaypoints = pluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> _pathMoveTo = pluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
    private readonly ICallGateSubscriber<float, object> _pathSetTolerance = pluginInterface.GetIpcSubscriber<float, object>("vnavmesh.Path.SetTolerance");
    private readonly ICallGateSubscriber<object> _pathStop = pluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> _queryPointOnFloor =
        pluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> _nearestPointReachable =
        pluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");

    public bool? GetPathRunning() => ReadOptional<bool>(() => _pathIsRunning.InvokeFunc());

    public bool IsPathRunning => GetPathRunning() != false;

    public bool? GetMovementAllowed() => ReadOptional(() => _movementAllowed.HasFunction ? (bool?)_movementAllowed.InvokeFunc() : null);
    public bool? GetSimpleMoveInProgress() => ReadOptional(() => _simpleMoveInProgress.HasFunction ? (bool?)_simpleMoveInProgress.InvokeFunc() : null);
    public bool? GetPathfindInProgress() => ReadOptional(() => _pathfindInProgress.HasFunction ? (bool?)_pathfindInProgress.InvokeFunc() : null);
    public int? GetQueuedPathfinds() => ReadOptional(() => _queuedPathfinds.HasFunction ? (int?)_queuedPathfinds.InvokeFunc() : null);

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

        IpcInvoke.SafeAction(() => _pathStop.InvokeAction(), logger,
            "Could not stop navigating via navmesh {Version}", GetVersion());
    }

    public Task<List<Vector3>> Pathfind(Vector3 localPlayerPosition, Vector3 targetPosition, bool fly,
        CancellationToken cancellationToken)
    {
        try
        {
            _pathSetTolerance.InvokeAction(0.25f);
            return _navPathfind.InvokeFunc(localPlayerPosition, targetPosition, fly, cancellationToken);
        }
        catch (Exception e)
        {
            return Task.FromException<List<Vector3>>(e);
        }
    }

    public void MoveTo(List<Vector3> position, bool fly)
    {
        _pathStop.InvokeAction();
        _pathMoveTo.InvokeAction(position, fly);
    }

    public Vector3? GetPointOnFloor(Lumina.Excel.Sheets.Level level) =>
        GetPointOnFloor(new Vector3(level.X, level.Y, level.Z), unlandable: false);

    public Vector3? GetPointOnFloor(Vector3 position, bool unlandable, float range = 0.2f) =>
        ReadOptional(() => _queryPointOnFloor.InvokeFunc(position, unlandable, range));

    public Vector3? GetNearestPointReachable(Vector3 position, float halfExtentXZ, float halfExtentY) =>
        ReadOptional(() => _nearestPointReachable.InvokeFunc(position, halfExtentXZ, halfExtentY));

    public List<Vector3> GetWaypoints()
    {
        if (GetPathRunning() != true)
            return [];
        return IpcInvoke.SafeFunc<List<Vector3>>(() => _pathListWaypoints.InvokeFunc(), []) ?? [];
    }

    public int GetBuildProgress() =>
        IpcInvoke.SafeFunc(() =>
        {
            float progress = _buildProgress.InvokeFunc();
            return progress < 0 ? 100 : (int)(progress * 100);
        }, 0);
}
