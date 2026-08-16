using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
namespace Questionable.External;

internal sealed class NavmeshIpc(IDalamudPluginInterface pluginInterface, ILogger<NavmeshIpc> logger)
{
    private readonly ICallGateSubscriber<float> _buildProgress = pluginInterface.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
    private readonly ICallGateSubscriber<bool> _isNavReady = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
    private readonly ICallGateSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>> _navPathfind =
        pluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>(
            "vnavmesh.Nav.PathfindCancelable");
    private readonly ICallGateSubscriber<bool> _pathIsRunning = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
    private readonly ICallGateSubscriber<List<Vector3>> _pathListWaypoints = pluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> _pathMoveTo = pluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
    private readonly ICallGateSubscriber<float, object> _pathSetTolerance = pluginInterface.GetIpcSubscriber<float, object>("vnavmesh.Path.SetTolerance");
    private readonly ICallGateSubscriber<object> _pathStop = pluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> _queryPointOnFloor =
        pluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> _simpleMovePathfindAndMoveCloseTo =
        pluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
    private readonly ICallGateSubscriber<Vector3, bool, bool> _simpleMovePathfindAndMoveTo =
        pluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
    private readonly ICallGateSubscriber<bool> _simpleMovePathfindInProgress =
        pluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");

    public Version? Version
    {
        get
        {
            IExposedPlugin? plugin = pluginInterface.InstalledPlugins.FirstOrDefault(x =>
                x.InternalName == "vnavmesh" && x.IsLoaded);
            return plugin?.Version ?? null;
        }
    }

    public bool IsReady => IpcInvoke.SafeFunc(() => _isNavReady.InvokeFunc(), fallback: false);

    public bool IsPathRunning => IpcInvoke.SafeFunc(() => _pathIsRunning.InvokeFunc(), fallback: false);

    public bool IsSimpleMovePathfindInProgress =>
        IpcInvoke.SafeFunc(() => _simpleMovePathfindInProgress.InvokeFunc(), fallback: false);

    public void Stop()
    {
        if (Version == null)
            return;

        IpcInvoke.SafeAction(() => _pathStop.InvokeAction(), logger,
            "Could not stop navigating via navmesh {Version}", Version);
    }

    public Task<List<Vector3>> Pathfind(Vector3 localPlayerPosition, Vector3 targetPosition, bool fly,
        CancellationToken cancellationToken)
    {
        try
        {
            _pathSetTolerance.InvokeAction(0.25f);
            return _navPathfind.InvokeFunc(localPlayerPosition, targetPosition, fly, cancellationToken);
        }
        catch (IpcNotReadyError e)
        {
            logger.LogWarning(e, "Could not pathfind via navmesh {Version}", Version);
            return Task.FromException<List<Vector3>>(e);
        }
    }

    public void MoveTo(List<Vector3> position, bool fly)
    {
        Stop();
        IpcInvoke.SafeAction(() => _pathMoveTo.InvokeAction(position, fly), logger,
            "Could not move via navmesh {Version}", Version);
    }

    public Vector3? GetPointOnFloor(Vector3 position, bool unlandable) =>
        IpcInvoke.SafeFunc(() => _queryPointOnFloor.InvokeFunc(position, unlandable, 0.2f), fallback: null);

    public bool SimplePathfindAndMoveTo(Vector3 destination, bool fly)
    {
        if (!IsReady)
            return false;
        return IpcInvoke.SafeFunc(() => _simpleMovePathfindAndMoveTo.InvokeFunc(destination, fly), fallback: false,
            logger, "Could not SimplePathfindAndMoveTo {Version}", Version);
    }

    public bool SimplePathfindAndMoveCloseTo(Vector3 destination, bool fly, float range)
    {
        if (!IsReady)
            return false;
        return IpcInvoke.SafeFunc(() => _simpleMovePathfindAndMoveCloseTo.InvokeFunc(destination, fly, range), fallback: false,
            logger, "Could not SimplePathfindAndMoveCloseTo {Version}", Version);
    }

    public List<Vector3> GetWaypoints()
    {
        if (!IsPathRunning)
            return [];
        return IpcInvoke.SafeFunc<List<Vector3>>(() => _pathListWaypoints.InvokeFunc(), []);
    }

    public int GetBuildProgress() =>
        IpcInvoke.SafeFunc(() =>
        {
            float progress = _buildProgress.InvokeFunc();
            return progress < 0 ? 100 : (int)(progress * 100);
        }, 0);
}
