using System.ComponentModel;
using Dalamud.Plugin.Ipc;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class RotationSolverRebornIpc : Ipc
{
    private readonly Configuration _configuration;
    private readonly ILogger<RotationSolverRebornIpc> _logger;
    private readonly ICallGateSubscriber<StateCommandType, object> _changeMode;
    private bool _controlling;

    public RotationSolverRebornIpc(IDalamudPluginInterface pluginInterface, Configuration configuration,
        ILogger<RotationSolverRebornIpc> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _changeMode = pluginInterface.GetIpcSubscriber<StateCommandType, object>("RotationSolverReborn.ChangeOperatingMode");
    }

    public override string InternalName => "RotationSolver";
    public override Version? GetVersion() => IPCUtils.IPCSubscriber.Version(InternalName);
    public bool IsEnabled => _configuration.General.CombatModule == ECombatModule.RotationSolverReborn && IsReady();

    public override bool IsReady() => IpcInvoke.SafeFunc(() => _changeMode.HasAction, fallback: false);

    // Henched: RSR runs the rotation and Questionable picks the targets, as Henchman, AutoDuty and BOCCHI use it.
    public bool RotationAuto()
    {
        if (!IsEnabled)
            return false;

        _controlling = SetMode(StateCommandType.Henched);
        return _controlling;
    }

    public bool RotationPause() => SetMode(StateCommandType.Off);

    public bool RotationResume() => SetMode(StateCommandType.Henched);

    public bool RotationStop()
    {
        if (!_controlling)
            return true;

        _controlling = !SetMode(StateCommandType.Off);
        return !_controlling;
    }

    public void VerifyActive()
    {
        if (_controlling && !IsReady())
            throw new TaskException("RotationSolverReborn is no longer available.");
    }

    private bool SetMode(StateCommandType mode)
    {
        try
        {
            _changeMode.InvokeAction(mode);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not switch RotationSolverReborn to {Mode}", mode);
            return false;
        }
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
