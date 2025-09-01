using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Questionable.External;

internal sealed class NavmeshIpc
{
    private readonly ILogger<NavmeshIpc> _logger;
    private readonly ICallGateSubscriber<bool> _isNavReady;
    private readonly ICallGateSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>> _navPathfind;
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> _pathMoveTo;
    private readonly ICallGateSubscriber<object> _pathStop;
    private readonly ICallGateSubscriber<bool> _pathIsRunning;
    private readonly ICallGateSubscriber<List<Vector3>> _pathListWaypoints;
    private readonly ICallGateSubscriber<float, object> _pathSetTolerance;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> _queryPointOnFloor;
    private readonly ICallGateSubscriber<float> _buildProgress;

    public NavmeshIpc(IDalamudPluginInterface pluginInterface, ILogger<NavmeshIpc> logger)
    {
        _logger = logger;
        _isNavReady = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        _navPathfind =
            pluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>(
                "vnavmesh.Nav.PathfindCancelable");
        _pathMoveTo = pluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        _pathStop = pluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        _pathIsRunning = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        _pathListWaypoints = pluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");
        _pathSetTolerance = pluginInterface.GetIpcSubscriber<float, object>("vnavmesh.Path.SetTolerance");
        _queryPointOnFloor =
            pluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        _buildProgress = pluginInterface.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
    }

    public bool IsReady
    {
        get
        {
            try
            {
                return _isNavReady.InvokeFunc();
            }
            catch (IpcError)
            {
                return false;
            }
        }
    }

    public bool IsPathRunning
    {
        get
        {
            try
            {
                return _pathIsRunning.InvokeFunc();
            }
            catch (IpcError)
            {
                return false;
            }
        }
    }

    public void Stop()
    {
        try
        {
            _pathStop.InvokeAction();
        }
        catch (IpcError e)
        {
            _logger.LogWarning(e, "Could not stop navigating via navmesh");
        }
    }

    public Task<List<Vector3>> Pathfind(Vector3 localPlayerPosition, Vector3 targetPosition, bool fly,
        CancellationToken cancellationToken)
    {
        try
        {
            _pathSetTolerance.InvokeAction(0.25f);
            return _navPathfind.InvokeFunc(localPlayerPosition, targetPosition, fly, cancellationToken);
        }
        catch (IpcError e)
        {
            _logger.LogWarning(e, "Could not pathfind via navmesh");
            return Task.FromException<List<Vector3>>(e);
        }
    }

    public void MoveTo(List<Vector3> position, bool fly)
    {
        Stop();

        try
        {
            _pathMoveTo.InvokeAction(position, fly);
        }
        catch (IpcError e)
        {
            _logger.LogWarning(e, "Could not move via navmesh");
        }
    }

    public Vector3? GetPointOnFloor(Vector3 position, bool unlandable)
    {
        try
        {
            return _queryPointOnFloor.InvokeFunc(position, unlandable, 0.2f);
        }
        catch (IpcError)
        {
            return null;
        }
    }

    public List<Vector3> GetWaypoints()
    {
        if (IsPathRunning)
        {
            try
            {
                return _pathListWaypoints.InvokeFunc();
            }
            catch (IpcError)
            {
                return [];
            }
        }
        else
            return [];
    }

    public int GetBuildProgress()
    {
        try
        {
            float progress = _buildProgress.InvokeFunc();
            if (progress < 0)
                return 100;
            return (int)(progress * 100);
        }
        catch (IpcError)
        {
            return 0;
        }
    }
}
