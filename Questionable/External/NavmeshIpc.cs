using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Microsoft.Extensions.Logging;
namespace Questionable.External;

internal sealed class NavmeshIpc(IDalamudPluginInterface pluginInterface, ILogger<NavmeshIpc> logger)
{
    private readonly ICallGateSubscriber<float> _buildProgress = pluginInterface.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
    private readonly ICallGateSubscriber<bool> _isNavReady = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
    private readonly ILogger<NavmeshIpc> _logger = logger;
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

    public bool IsSimpleMovePathfindInProgress
    {
        get
        {
            try
            {
                return _simpleMovePathfindInProgress.InvokeFunc();
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
            IExposedPlugin? plugin = pluginInterface.InstalledPlugins.FirstOrDefault(x =>
                x.InternalName == "vnavmesh" && x.IsLoaded);
            if (plugin != null && plugin.Version < new Version(1, 2, 3, 2))
                throw new IpcValueNullError("vnavmesh", typeof(Version), 0);
            _pathSetTolerance.InvokeAction(0.25f);
            return _navPathfind.InvokeFunc(localPlayerPosition, targetPosition, fly, cancellationToken);
        }
        catch (IpcNotReadyError e)
        {
            _logger.LogWarning(e, "Could not pathfind via navmesh");
            return Task.FromException<List<Vector3>>(e);
        }
        catch (IpcValueNullError e)
        {
            _logger.LogWarning(e, "Unsupported version of vnavmesh");
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

    public bool SimplePathfindAndMoveTo(Vector3 destination, bool fly)
    {
        if (!IsReady)
            return false;
        try
        {
            return _simpleMovePathfindAndMoveTo.InvokeFunc(destination, fly);
        }
        catch (IpcError exception)
        {
            _logger.LogWarning(exception, "Could not SimplePathfindAndMoveTo");
            return false;
        }
    }

    public bool SimplePathfindAndMoveCloseTo(Vector3 destination, bool fly, float range)
    {
        if (!IsReady)
            return false;
        try
        {
            return _simpleMovePathfindAndMoveCloseTo.InvokeFunc(destination, fly, range);
        }
        catch (IpcError exception)
        {
            _logger.LogWarning(exception, "Could not SimplePathfindAndMoveCloseTo");
            return false;
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
