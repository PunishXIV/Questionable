using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using Questionable.Model.Questing;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class AutoDutyIpc : Ipc, IDisposable
{
    private const string UnsyncedKey = "Meta.Unsynced";
    private const string DutyModeKey = "Meta.DutyModeEnum";
    private const string PreAction = "Loop.Pre.Actions.";
    internal const string ProfileName = "Questionable";

    internal enum SettingKind
    {
        Required,
        WantOn,
        ForceOff,
    }

    internal sealed record ProfileSetting(string Key, string Value, SettingKind Kind = SettingKind.Required);

    // Every loop action type except the playlist ones, which AutoDuty runs regardless of these settings.
    // Asking for a type the list doesn't hold logs an error in AutoDuty; it has no way to list or reset them.
    private static readonly string[] LoopActionTypes =
    [
        "AutoEquip", "Repair", "AutoRetainer", "AutoRetainerMultiMode", "DiscardItems", "TripleTriadUse",
        "TripleTriadSell", "Armoire", "Glamour", "Extract", "GCTurnIn", "Desynth", "CofferOpen", "Shop",
        "Retire", "ConsumeItems", "ExecuteCommands", "Wait", "PlaySound",
    ];

    // The profile starts as a copy of the user's default, so everything that could stall or harm a quest run is forced.
    internal static readonly IReadOnlyList<ProfileSetting> ProfileSettings =
    [
        new("Loop.Pre.Enabled", "True"),
        new("Loop.Between.Enabled", "False"),
        new("Loop.Termination.Enabled", "False"),
        new("Loop.Termination.TerminationMethodEnum", "Do_Nothing"),
        new("DutyConfig.AutoExitDuty", "True"),
        new("DutyConfig.OnlyExitWhenDutyDone", "True"),
        new("DutyConfig.AutoManageRotationPluginState", "True"),
        new("DutyConfig.RotationPlugin", "All"),
        new("DutyConfig.AutoManageBossModAISettings", "True"),
        new("DutyConfig.AutoManageVnavAlignCamera", "False"),
        new("DutyConfig.LootTreasure", "True"),
        new("DutyConfig.LootMethodEnum", "All"),
        new("DutyConfig.LootBossTreasureOnly", "False"),
        new("DutyConfig.Stuck.MinStuckTime", "500"),
        new("DutyConfig.Stuck.StuckOnStep", "True"),
        new("DutyConfig.Stuck.RebuildNavmeshOnStuck", "True"),
        new("DutyConfig.Stuck.RebuildNavmeshAfterStuckXTimes", "5"),
        new("DutyConfig.Stuck.StuckReturn", "False"),
        new("DutyConfig.WaitBeforeLeaving", "0"),
        new("DutyConfig.PathDrawEnabled", "True"),
        new("DutyConfig.PathDrawStepCount", "5"),
        new($"{PreAction}[RepairLoopActionConfig].Enabled", "True", SettingKind.WantOn),
        new($"{PreAction}[RepairLoopActionConfig].AutoRepairPct", "50", SettingKind.WantOn),
        new($"{PreAction}[RepairLoopActionConfig].AutoRepairSelf", "False", SettingKind.WantOn),
        new($"{PreAction}[AutoEquipLoopActionConfig].Enabled", "True", SettingKind.WantOn),
        new($"{PreAction}[AutoEquipLoopActionConfig].RecommendedGearSource", "Vanilla", SettingKind.WantOn),
        new($"{PreAction}[ExtractLoopActionConfig].Enabled", "True", SettingKind.WantOn),
        new($"{PreAction}[ExtractLoopActionConfig].AutoExtractAll", "False", SettingKind.WantOn),
        .. ForceOffExcept(PreAction, "Repair", "AutoEquip", "Extract"),
    ];

    private static IEnumerable<ProfileSetting> ForceOffExcept(string list, params string[] wanted) =>
        LoopActionTypes.Except(wanted).Select(type =>
            new ProfileSetting($"{list}[{type}LoopActionConfig].Enabled", "False", SettingKind.ForceOff));

    private readonly Configuration _configuration;
    private readonly Func<uint, TerritoryData.ContentFinderConditionData?> _findContent;
    private readonly Func<AutoDutyGameState> _gameState;
    private readonly ILogger<AutoDutyIpc> _logger;
    private readonly IDutyState? _dutyState;
    private readonly IDutyState.DutyCompletedDelegate? _dutyCompleted;
    private readonly ICallGateSubscriber<uint, bool> _contentHasPath;
    private readonly ICallGateSubscriber<bool> _isStopped;
    private readonly ICallGateSubscriber<uint, int, bool, object> _run;
    private readonly ICallGateSubscriber<string, uint, int, bool, object> _runForPlugin;
    private readonly ICallGateSubscriber<string, bool, object> _pluginConfig;
    private readonly ICallGateSubscriber<string, string> _getConfig;
    private readonly ICallGateSubscriber<string, object, object> _setConfig;
    private readonly ICallGateSubscriber<object> _stop;
    private readonly ICallGateSubscriber<bool> _pathRunning;
    private readonly ICallGateSubscriber<bool> _pathFinding;
    private readonly ICallGateSubscriber<bool> _simplePathFinding;
    private bool _loggedReleaseFailure;

    public AutoDutyIpc(IDalamudPluginInterface pluginInterface, Configuration configuration,
        TerritoryData territoryData, ILogger<AutoDutyIpc> logger, AutoDutyOperationState operation,
        IClientState clientState, ICondition condition, IDutyState dutyState)
        : this(pluginInterface, configuration,
            id => territoryData.TryGetContentFinderCondition(id, out var data) ? data : null,
            logger, operation, () => new AutoDutyGameState(clientState.IsLoggedIn, clientState.TerritoryType,
                condition[ConditionFlag.InDutyQueue],
                condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51],
                condition[ConditionFlag.BoundByDuty],
                !condition[ConditionFlag.InCombat] && !condition[ConditionFlag.Casting] &&
                !condition[ConditionFlag.Occupied] && !condition[ConditionFlag.OccupiedInCutSceneEvent] &&
                !condition[ConditionFlag.WatchingCutscene] && !condition[ConditionFlag.Unconscious]), dutyState)
    {
    }

    internal AutoDutyIpc(IDalamudPluginInterface pluginInterface, Configuration configuration,
        Func<uint, TerritoryData.ContentFinderConditionData?> findContent, ILogger<AutoDutyIpc> logger,
        AutoDutyOperationState operation, Func<AutoDutyGameState> gameState, IDutyState? dutyState = null)
    {
        _configuration = configuration;
        _findContent = findContent;
        _logger = logger;
        Operation = operation;
        _gameState = gameState;
        _contentHasPath = pluginInterface.GetIpcSubscriber<uint, bool>("AutoDuty.ContentHasPath");
        _isStopped = pluginInterface.GetIpcSubscriber<bool>("AutoDuty.IsStopped");
        _run = pluginInterface.GetIpcSubscriber<uint, int, bool, object>("AutoDuty.Run");
        _runForPlugin = pluginInterface.GetIpcSubscriber<string, uint, int, bool, object>("AutoDuty.RunForPlugin");
        _pluginConfig = pluginInterface.GetIpcSubscriber<string, bool, object>("AutoDuty.PluginConfig");
        _getConfig = pluginInterface.GetIpcSubscriber<string, string>("AutoDuty.GetConfig");
        _setConfig = pluginInterface.GetIpcSubscriber<string, object, object>("AutoDuty.SetConfig");
        _stop = pluginInterface.GetIpcSubscriber<object>("AutoDuty.Stop");
        _pathRunning = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        _pathFinding = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        _simplePathFinding = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        _dutyState = dutyState;
        if (_dutyState != null)
        {
            _dutyCompleted = _ =>
            {
                var game = _gameState();
                if (game.Available)
                    Operation.ObserveDutyCompleted(game.TerritoryId);
            };
            _dutyState.DutyCompleted += _dutyCompleted;
        }
    }

    public override string InternalName => "AutoDuty";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => GetStopped() != null && _contentHasPath.HasFunction &&
        _getConfig.HasFunction && _setConfig.HasAction && SupportsPluginProfile();

    private bool SupportsPluginProfile() => _pluginConfig.HasAction && _runForPlugin.HasAction;

    public bool IsAvailable()
    {
        try
        {
            return _run.HasAction || _stop.HasAction || _isStopped.HasFunction;
        }
        catch (Exception)
        {
            // A failed endpoint lookup does not prove absence; protect possible manual work.
            return true;
        }
    }

    public AutoDutyOperationState Operation { get; }
    public bool HasOperation => Operation.HasOperation;
    public string? BlockReason => Operation.BlockReason;
    public bool RequiresReconciliation => Operation.RequiresReconciliation;
    public bool CanConfirmIdle => Operation.CanConfirmIdle;

    [Flags]
    public enum DutyMode : int
    {
        None = 0 << 0,
        Support = 1 << 0,
        Trust = 1 << 1,
        Squadron = 1 << 2,
        Regular = 1 << 3,
        Trial = 1 << 4,
        Raid = 1 << 5,
        Variant = 1 << 6,
        NoviceHall = 1 << 7
    }

    public bool IsConfiguredToRunContent(DutyOptions? dutyOptions) =>
        dutyOptions is { ContentFinderConditionId: > 0 } &&
        _configuration.Duties.RunInstancedContentWithAutoDuty &&
        !_configuration.Duties.BlacklistedDutyCfcIds.Contains(dutyOptions.ContentFinderConditionId) &&
        (dutyOptions.Enabled || _configuration.Duties.WhitelistedDutyCfcIds.Contains(dutyOptions.ContentFinderConditionId)) &&
        HasPath(dutyOptions.ContentFinderConditionId);

    public bool HasPath(uint cfcId)
    {
        var data = _findContent(cfcId);
        return data != null && IpcInvoke.SafeFunc(() => _contentHasPath.InvokeFunc(data.TerritoryId), false,
            _logger, "Unable to query AutoDuty for path in territory {TerritoryType}", data.TerritoryId);
    }

    public void StartInstance(uint cfcId, DutyMode dutyMode, bool pathEnabled = true)
    {
        if (HasOperation)
            throw new TaskException(BlockReason!);
        if (!IsConfiguredToRunContent(new DutyOptions { ContentFinderConditionId = cfcId, Enabled = pathEnabled }))
            throw new TaskException("AutoDuty is disabled or this duty is excluded or unsupported.");
        var data = _findContent(cfcId) ?? throw new TaskException($"Unknown ContentFinderConditionId {cfcId}");
        if (GetStopped() != null && !SupportsPluginProfile())
            throw new TaskException("Update AutoDuty: this version can't run duties in a separate Questionable profile.");
        if (!IsReady() || GetStopped() != true)
            throw new TaskException("AutoDuty is unavailable or busy; refusing to replace its work.");
        var game = ReadGameState();
        if (!game.Available || !game.Idle || game.InQueue || game.Loading || game.InDuty)
            throw new TaskException("Cannot start AutoDuty while native duty, queue or movement is active.");

        Operation.BeginSubmission(data.TerritoryId);
        _loggedReleaseFailure = false;
        try
        {
            // Before SetConfig, so it writes to our profile instead of the user's.
            _pluginConfig.InvokeAction(ProfileName, true);
            ApplySetting(new ProfileSetting(UnsyncedKey, (dutyMode == DutyMode.Regular).ToString()));
            ApplySetting(new ProfileSetting(DutyModeKey, dutyMode.ToString()));
            foreach (var setting in ProfileSettings)
                ApplySetting(setting);
            if (GetStopped() != true)
            {
                Operation.RequireReconciliation("AutoDuty became busy during configuration; its work is not owned");
                throw new TaskException("AutoDuty became busy during configuration.");
            }
            Operation.MarkRunSubmitted();
            _runForPlugin.InvokeAction(ProfileName, data.TerritoryId, 1, false);
            // Run returns nothing; watch for activity instead of retrying it.
            PollOperation();
        }
        catch (Exception e)
        {
            Operation.RequireReconciliation(Operation.RunSubmitted
                ? "AutoDuty submission is uncertain; check its work before restarting"
                : "AutoDuty configuration failed; its profile will be switched back while idle");
            throw new TaskException(_LF("Unable to run content with AutoDuty: {0}", e.Message), e);
        }
    }

    public bool? GetStopped()
    {
        try
        {
            return _isStopped.HasFunction && _run.HasAction && _stop.HasAction ? _isStopped.InvokeFunc() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool IsStopped() => GetStopped() == true;

    public void PollOperation()
    {
        if (!HasOperation)
            return;
        Operation.Observe(GetStopped(), ReadGameState());
        if (!Operation.CanReleaseProfile)
            return;
        if (ReleaseProfile())
        {
            Operation.ProfileReleased();
            Operation.FinishIfSettled();
        }
    }

    public void CancelOwned()
    {
        if (!HasOperation)
            return;
        PollOperation();
        if (!Operation.BeginStop())
            return;
        try
        {
            // Stop is global, so only send it while our run owns AutoDuty.
            _stop.InvokeAction();
        }
        catch (Exception e)
        {
            Operation.RequireReconciliation("AutoDuty stop request failed; stopping is not confirmed");
            _logger.LogWarning(e, "Unable to confirm the AutoDuty stop request");
        }
    }

    public void Stop() => CancelOwned();
    public void ObserveIdle() => PollOperation();
    public bool TryConfirmIdle()
    {
        PollOperation();
        return Operation.TryConfirmIdle();
    }

    private AutoDutyGameState ReadGameState()
    {
        try
        {
            var state = _gameState();
            return state with { Idle = state.Idle && !_pathRunning.InvokeFunc() &&
                !_pathFinding.InvokeFunc() && !_simplePathFinding.InvokeFunc() };
        }
        catch (Exception)
        {
            return default;
        }
    }

    // Read first: every write makes AutoDuty save the profile to disk, and an unknown key reads back empty.
    private void ApplySetting(ProfileSetting setting)
    {
        string current = _getConfig.InvokeFunc(setting.Key) ?? string.Empty;
        if (current.Length == 0)
        {
            switch (setting.Kind)
            {
                case SettingKind.ForceOff:
                    return;
                case SettingKind.WantOn:
                    _logger.LogWarning("AutoDuty's {Profile} profile has no {Key}; running without it", ProfileName, setting.Key);
                    return;
                default:
                    throw new InvalidOperationException($"Update AutoDuty: it doesn't support {setting.Key}.");
            }
        }

        if (Matches(current, setting.Value))
            return;
        _setConfig.InvokeAction(setting.Key, setting.Value);
        if (!Matches(_getConfig.InvokeFunc(setting.Key), setting.Value))
            throw new InvalidOperationException($"AutoDuty did not apply {setting.Key}.");
    }

    private static bool Matches(string? actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    // AutoDuty switches back itself when a run stops; this covers runs that never started.
    private bool ReleaseProfile()
    {
        try
        {
            _pluginConfig.InvokeAction(ProfileName, false);
            return true;
        }
        catch (Exception e)
        {
            Operation.RequireReconciliation("AutoDuty didn't switch back from Questionable's profile; this will be retried while idle");
            if (!_loggedReleaseFailure)
            {
                _logger.LogWarning(e, "Unable to switch AutoDuty back from Questionable's profile");
                _loggedReleaseFailure = true;
            }
            return false;
        }
    }

    public void Dispose()
    {
        if (_dutyState != null && _dutyCompleted != null)
            _dutyState.DutyCompleted -= _dutyCompleted;
        // The controller attempts cancellation on the framework thread before disposal.
    }
}
