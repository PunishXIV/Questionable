using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton<IGatherChillIpc, GatherChillIpc>]
internal sealed class GatherChillIpc : Ipc, IGatherChillIpc
{
    private readonly ILogger<GatherChillIpc> _logger;
    private readonly GatheringOperationState _operation;
    private readonly Func<bool?> _gameIdle;
    private readonly Func<bool> _hasLifestream;
    private readonly ICallGateSubscriber<int> _apiVersion;
    private readonly ICallGateSubscriber<bool> _isBusy;
    private readonly ICallGateSubscriber<uint, bool> _canGather;
    private readonly ICallGateSubscriber<uint, int, bool> _gatherItem;
    private readonly ICallGateSubscriber<object> _stop;
    private readonly ICallGateSubscriber<bool> _pathRunning;
    private readonly ICallGateSubscriber<bool> _pathFinding;
    private readonly ICallGateSubscriber<bool> _simplePathFinding;
    private readonly ICallGateSubscriber<bool> _lifestreamBusy;

    public GatherChillIpc(IDalamudPluginInterface pluginInterface, ILogger<GatherChillIpc> logger,
        GatheringOperationState operation, IClientState clientState, ICondition condition)
        : this(pluginInterface, logger, operation, () => clientState.IsLoggedIn
            ? !condition[ConditionFlag.Gathering] && !condition[ConditionFlag.Casting] &&
              !condition[ConditionFlag.BetweenAreas] && !condition[ConditionFlag.BetweenAreas51]
            : null)
    {
    }

    internal GatherChillIpc(IDalamudPluginInterface pluginInterface, ILogger<GatherChillIpc> logger,
        GatheringOperationState operation, Func<bool?> gameIdle)
    {
        _logger = logger;
        _operation = operation;
        _gameIdle = gameIdle;
        _hasLifestream = () => pluginInterface.InstalledPlugins.Any(p => p.InternalName == "Lifestream" && p.IsLoaded);
        _apiVersion = pluginInterface.GetIpcSubscriber<int>("GatherChill.ApiVersion");
        _isBusy = pluginInterface.GetIpcSubscriber<bool>("GatherChill.IsBusy");
        _canGather = pluginInterface.GetIpcSubscriber<uint, bool>("GatherChill.CanGather");
        _gatherItem = pluginInterface.GetIpcSubscriber<uint, int, bool>("GatherChill.GatherItem");
        _stop = pluginInterface.GetIpcSubscriber<object>("GatherChill.Stop");
        _pathRunning = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        _pathFinding = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        _simplePathFinding = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        _lifestreamBusy = pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
    }

    public override string InternalName => "GatherChill";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public bool IsAvailable() => Read(() => _apiVersion.InvokeFunc() >= 2) == true;
    public override bool IsReady() => IsAvailable();
    public bool? GetBusy() => Read(_isBusy.InvokeFunc);
    public bool CanGather(uint itemId) => Read(() => _canGather.InvokeFunc(itemId)) == true;

    public GatherStartResult StartRequest(uint itemId, int additionalCount, out string message)
    {
        message = "";
        if (_operation.HasOperation)
            return GatherStartResult.Busy;
        if (itemId == 0 || additionalCount <= 0)
            return GatherStartResult.Invalid;
        if (!IsReady())
            return GatherStartResult.NotReady;
        if (GetBusy() != false)
            return GatherStartResult.Busy;

        Guid owner = _operation.BeginSubmission();
        try
        {
            _logger.LogInformation("Requesting {Count} additional {ItemId} from Gather n Chill", additionalCount, itemId);
            if (!_gatherItem.InvokeFunc(itemId, additionalCount))
            {
                _operation.RejectSubmission(owner);
                return GatherStartResult.Busy;
            }
            _operation.AcceptLegacySubmission(owner);
            return GatherStartResult.Started;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Gather n Chill submission reply unavailable");
            _operation.MarkLegacyUncertain(owner);
            message = e.Message;
            return GatherStartResult.Uncertain;
        }
    }

    public void PollOperation()
    {
        if (!_operation.HasOperation)
            return;
        _operation.ObserveLegacyStatus(GetBusy(), ReadChildrenIdle());
    }

    public void CancelOwned()
    {
        if (!_operation.HasOperation)
            return;
        Guid owner = _operation.OperationId!.Value;
        // The legacy stop is global. Send it at most once while this submission remains
        // known; provider loss or a lost start reply requires manual reconciliation instead.
        if (!_operation.BeginLegacyStop())
            return;
        bool? busy = GetBusy();
        if (busy == false)
            return;
        if (busy == null)
        {
            _operation.MarkLegacyUncertain(owner);
            return;
        }
        try
        {
            _stop.InvokeAction();
        }
        catch (Exception e)
        {
            _operation.MarkLegacyUncertain(owner);
            _logger.LogWarning(e, "Unable to confirm the Gather n Chill stop request");
        }
    }

    public void ObserveIdle() => PollOperation();

    private bool? ReadChildrenIdle()
    {
        try
        {
            bool? gameIdle = _gameIdle();
            if (gameIdle != true)
                return gameIdle;
            // Read each child before combining results: busy must not mask an unavailable child.
            bool running = _pathRunning.InvokeFunc();
            bool finding = _pathFinding.InvokeFunc();
            bool simpleFinding = _simplePathFinding.InvokeFunc();
            bool lifestream = _hasLifestream() && _lifestreamBusy.InvokeFunc();
            return !running && !finding && !simpleFinding && !lifestream;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool? Read(Func<bool> call)
    {
        try
        {
            return call();
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal interface IGatherChillIpc
{
    bool IsAvailable();
    bool IsReady();
    bool? GetBusy();
    bool CanGather(uint itemId);
    GatherStartResult StartRequest(uint itemId, int additionalCount, out string message);
    void PollOperation();
    void CancelOwned();
    void ObserveIdle();
}

internal enum GatherStartResult
{
    Uncertain = -1,
    Started = 0,
    Busy = 1,
    Unsupported = 2,
    Invalid = 3,
    NotReady = 4,
}
