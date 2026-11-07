using System.ComponentModel;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc;
using Questionable.Model.Common;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class RotationSolverRebornIpc : Ipc
{
    private readonly Configuration _configuration;
    private readonly ILogger<RotationSolverRebornIpc> _logger;
    private readonly IFramework _framework;
    private readonly ICallGateSubscriber<StateCommandType, object> _changeMode;
    private readonly ICallGateSubscriber<string, object> _test;
    private object? _controlled;
    private StateCommandType _previousMode;
    private StateCommandType _expectedMode = StateCommandType.Henched;
    private StateCommandType? _transitionFrom;
    private bool _controlLost;
    private bool _cleanupFailureLogged;
    private long _nextVerificationAt;

    public RotationSolverRebornIpc(IDalamudPluginInterface pluginInterface, Configuration configuration,
        ILogger<RotationSolverRebornIpc> logger, IFramework framework)
    {
        _configuration = configuration;
        _logger = logger;
        _framework = framework;
        _changeMode = pluginInterface.GetIpcSubscriber<StateCommandType, object>("RotationSolverReborn.ChangeOperatingMode");
        _test = pluginInterface.GetIpcSubscriber<string, object>("RotationSolverReborn.Test");
    }

    public override string InternalName => "RotationSolver";
    public override Version? GetVersion() => FindProvider()?.GetType().Assembly.GetName().Version;
    public bool IsEnabled => _configuration.General.CombatModule == ECombatModule.RotationSolverReborn && IsReady();

    public override bool IsReady()
    {
        try
        {
            EnsureFrameworkThread();
            object? provider = FindProvider();
            if (provider == null || !_changeMode.HasAction)
                return false;
            _ = ReadMode(provider);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "RotationSolverReborn is unavailable or its mode cannot be read");
            return false;
        }
    }

    public bool RotationAuto()
    {
        if (_configuration.General.CombatModule != ECombatModule.RotationSolverReborn || _controlled != null)
            return false;

        try
        {
            EnsureFrameworkThread();
            object? provider = FindProvider();
            if (provider == null || !_changeMode.HasAction)
                return false;
            _test.InvokeAction("Validate RSR is callable from Questionable");
            StateCommandType previous = ReadMode(provider);
            // These modes belong to another plugin; do not borrow its rotation.
            if (previous is StateCommandType.Henched or StateCommandType.AutoDuty)
                return false;
            _previousMode = previous;
            _expectedMode = StateCommandType.Henched;
            _transitionFrom = null;
            _controlLost = false;
            _cleanupFailureLogged = false;
            _controlled = provider;
            _changeMode.InvokeAction(StateCommandType.Henched);
            VerifyActiveCore();
            _nextVerificationAt = 0;
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not start RotationSolverReborn combat");
            return false;
        }
    }

    public bool RotationPause() => SetMode(StateCommandType.Off);

    public bool RotationResume() => SetMode(StateCommandType.Henched);

    private bool SetMode(StateCommandType mode)
    {
        try
        {
            EnsureFrameworkThread();
            VerifyActiveCore();
            _transitionFrom = _expectedMode;
            _expectedMode = mode;
            // Keep both possible states until readback succeeds: IPC may apply and then throw.
            _changeMode.InvokeAction(mode);
            if (ReadMode(_controlled!) != mode)
                throw new TaskException("RotationSolverReborn rejected the combat item rotation change.");
            _transitionFrom = null;
            _nextVerificationAt = 0;
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not change RotationSolverReborn mode for combat item use");
            return false;
        }
    }

    public bool RotationStop()
    {
        if (_controlled == null)
            return true;
        try
        {
            EnsureFrameworkThread();
            object? provider = FindProvider();
            if (provider == null)
            {
                _controlLost = true;
                return false;
            }
            if (!ReferenceEquals(_controlled, provider))
            {
                // The old RSR is gone; don't restore its snapshot into the new one.
                _controlled = null;
                return true;
            }
            StateCommandType current = ReadMode(provider);
            if (current != _expectedMode && current != _transitionFrom)
            {
                // A manual change (or an already-completed rollback) takes precedence.
                _controlled = null;
                return true;
            }
            if (_controlLost)
                return false;

            _changeMode.InvokeAction(_previousMode);
            if (ReadMode(provider) != _previousMode)
                return false;
            _controlled = null;
            return true;
        }
        catch (Exception e)
        {
            if (!_cleanupFailureLogged)
            {
                _logger.LogWarning(e, "Could not restore RotationSolverReborn mode; retaining cleanup for retry");
                _cleanupFailureLogged = true;
            }
            return false;
        }
    }

    public void VerifyActive()
    {
        EnsureFrameworkThread();
        if (_controlled == null || _controlLost)
            throw new TaskException("RotationSolverReborn combat control was lost.");
        // HasAction is cheap and catches endpoint removal without reflection or logging each frame.
        if (!_changeMode.HasAction)
        {
            _controlLost = true;
            throw new TaskException("RotationSolverReborn is no longer available.");
        }
        long now = Environment.TickCount64;
        if (now >= _nextVerificationAt)
        {
            VerifyActiveCore();
            _nextVerificationAt = now + 250;
        }
    }

    private void VerifyActiveCore()
    {
        object? provider = FindProvider();
        if (_controlled == null || provider == null || !ReferenceEquals(provider, _controlled))
        {
            _controlLost = true;
            throw new TaskException("RotationSolverReborn disappeared or was replaced.");
        }
        if (_controlLost || ReadMode(provider) != _expectedMode)
        {
            _controlLost = true;
            throw new TaskException("RotationSolverReborn combat control was lost.");
        }
    }

    private void EnsureFrameworkThread()
    {
        // Never wait on the framework thread from elsewhere; failed off-thread cleanup is retried by CombatController's poll.
        if (!_framework.IsInFrameworkUpdateThread)
            throw new TaskException("Rotation control must run on the framework thread; cleanup remains pending.");
    }

    private static object? FindProvider() =>
        DalamudReflector.TryGetDalamudPlugin("RotationSolver", out var plugin, suppressErrors: false, ignoreCache: true)
            ? plugin
            : null;

    internal static StateCommandType ReadMode(object provider)
    {
        // RSR has no GetOperatingMode IPC. AutorotationActive also returns false without a player.
        // Read the actual mode flags used by RSCommands.ApplyState; missing flags are unsupported.
        Type state = ResolveModeType(provider.GetType().Assembly);
        bool Read(string name) => state.GetProperty(name, BindingFlags.Static | BindingFlags.Public)?.GetValue(null) as bool?
            ?? throw new TaskException($"RotationSolverReborn mode property {name} is unavailable.");

        bool enabled = Read("State");
        bool manual = Read("IsManual");
        bool henched = Read("IsHenched");
        bool autoDuty = Read("IsAutoDuty");
        bool targetOnly = Read("IsTargetOnly");
        if (state.GetProperty("IsPvPStateEnabled", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) is true)
            throw new TaskException("RotationSolverReborn PvP mode cannot be used for quest combat.");
        if (!enabled && !manual)
            return StateCommandType.Off;
        if (!enabled)
            throw new TaskException("RotationSolverReborn mode is inconsistent.");
        return henched ? StateCommandType.Henched : autoDuty ? StateCommandType.AutoDuty :
            targetOnly ? StateCommandType.TargetOnly : manual ? StateCommandType.Manual : StateCommandType.Auto;
    }

    internal static Type ResolveModeType(Assembly providerAssembly)
    {
        const string typeName = "RotationSolver.Basic.DataCenter";
        if (providerAssembly.GetType(typeName) is { } localType)
            return localType;

        // Before 2026-10-28 Basic was a separate project. Only resolve the exact reference in the
        // provider's own load context; another plugin/reloaded instance may have the same type name.
        AssemblyName? reference = providerAssembly.GetReferencedAssemblies()
            .FirstOrDefault(x => x.Name == "RotationSolver.Basic");
        if (reference != null && AssemblyLoadContext.GetLoadContext(providerAssembly) is { } context)
        {
            Assembly? companion = context.Assemblies.FirstOrDefault(x => x.GetName().FullName == reference.FullName);
            if (companion?.GetType(typeName) is { } companionType)
                return companionType;
        }
        throw new TaskException("This RotationSolverReborn version does not expose readable mode state.");
    }

    public enum StateCommandType : byte
    {
        [Description("Stop the addon. Always remember to turn it off when it is not in use!")]
        Off,

        [Description("Start the addon in Auto mode. When out of combat or when combat starts, switches the target according to the set condition. " +
                     "\r\n Optionally: You can add the target type to the end of the command you want RSR to do. For example: /rotation Auto Big")]
        Auto,

        [Description("Start in Target-Only mode. RSR will auto-select targets per normal logic but will not perform any actions.")]
        TargetOnly,

        [Description("Start the addon in Manual mode. You need to choose the target manually. This will bypass any engage settings that you have set up and will start attacking immediately once something is targeted.")]
        Manual,

        [Description("This mode is managed by the AutoDuty plugin")]
        AutoDuty,

        [Description("This mode is managed by the Henchman plugin, or any other plugin that requires RSR just do rotation and not targetting.")]
        Henched,
    }
}

[RegisterSingleton<ICombatModule, RotationSolverRebornModule>(Duplicate = DuplicateStrategy.Append)]
internal sealed class RotationSolverRebornModule(RotationSolverRebornIpc rotationSolverRebornIpc) : ICombatModule, IDisposable
{
    public string Name => "Rotation Solver Reborn";

    public bool CanHandleFight(CombatController.CombatData combatData) => rotationSolverRebornIpc.IsEnabled;

    public bool Start(CombatController.CombatData combatData) => rotationSolverRebornIpc.RotationAuto();

    public bool Stop() => rotationSolverRebornIpc.RotationStop();

    public bool Pause() => rotationSolverRebornIpc.RotationPause();

    public bool Resume() => rotationSolverRebornIpc.RotationResume();

    public void VerifyActive() => rotationSolverRebornIpc.VerifyActive();

    public void Update(IGameObject gameObject)
    {
    }

    public bool CanAttack(IBattleNpc target) => true;

    public void Dispose() => Stop();
}
