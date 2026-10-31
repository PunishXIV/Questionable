using System.ComponentModel;
using Dalamud.Plugin.Ipc;
using Questionable.Model.Common;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class RotationSolverRebornIpc(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    ILogger<RotationSolverRebornIpc> logger,
    IFramework framework) : Ipc
{
    public override string InternalName => "RotationSolverReborn";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() =>
        {
            var _ = Test.InvokeFunc("Validate RSR is callable from Questionable");
            return GetVersion() != null;
        }, fallback: false);
    private readonly ICallGateSubscriber<StateCommandType, object> ChangeOperatingMode =
        pluginInterface.GetIpcSubscriber<StateCommandType, object>("RotationSolverReborn.ChangeOperatingMode");
    private readonly ICallGateSubscriber<string, object> Test =
        pluginInterface.GetIpcSubscriber<string, object>("RotationSolverReborn.Test");
    public bool IsEnabled => IsReady("RotationSolver");

    public bool RotationAuto()
    {
        if (configuration.General.CombatModule != ECombatModule.RotationSolverReborn)
            return false;
        InvokeChangeOperatingMode(StateCommandType.Henched);
        return true;
    }

    public void RotationStop()
    {
        if (configuration.General.CombatModule != ECombatModule.RotationSolverReborn)
            return;
        InvokeChangeOperatingMode(StateCommandType.Off);
    }

    private void InvokeChangeOperatingMode(StateCommandType mode) =>
        IpcInvoke.TryOnFrameworkThread(framework, () =>
        {
            _ = IpcInvoke.SafeFunc(() =>
            {
                ChangeOperatingMode.InvokeAction(mode);
                return true;
            }, fallback: false);
        }, logger);

    internal void Dispose() => RotationStop();
    internal bool IsReady(string pluginName)
    {
        if (configuration.General.CombatModule != ECombatModule.RotationSolverReborn)
            return false;
        try
        {
            Test.InvokeAction("Validate RSR is callable from Questionable");
            return DalamudReflector.TryGetDalamudPlugin(pluginName, out var dalamudPlugin, suppressErrors: false, ignoreCache: true);
        }
        catch (Exception ex)
        {
            // Avoid throwing from framework update tick; treat as not ready when reflection isn't initialized
            logger.LogDebug($"RotationSolverRebornIpc.IsReady: reflection check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The state of the plugin.
    /// </summary>
    public enum StateCommandType : byte
    {
        /// <summary>
        /// Stop the addon. Always remember to turn it off when it is not in use!
        /// </summary>
        [Description("Stop the addon. Always remember to turn it off when it is not in use!")]
        Off,

        /// <summary>
        /// Start the addon in Auto mode. When out of combat or when combat starts, switches the target according to the set condition.
        /// </summary>
        [Description("Start the addon in Auto mode. When out of combat or when combat starts, switches the target according to the set condition. " +
                     "\r\n Optionally: You can add the target type to the end of the command you want RSR to do. For example: /rotation Auto Big")]
        Auto,

        /// <summary>
        /// Start the addon in Target-Only mode. RSR will auto-select targets per normal logic but will not perform any actions.
        /// </summary>
        [Description("Start in Target-Only mode. RSR will auto-select targets per normal logic but will not perform any actions.")]
        TargetOnly,

        /// <summary>
        /// Start the addon in Manual mode. You need to choose the target manually. This will bypass any engage settings that you have set up and will start attacking immediately once something is targeted.
        /// </summary>
        [Description("Start the addon in Manual mode. You need to choose the target manually. This will bypass any engage settings that you have set up and will start attacking immediately once something is targeted.")]
        Manual,

        [Description("This mode is managed by the AutoDuty plugin")]
        AutoDuty,

        [Description("This mode is managed by the Henchman plugin, or any other plugin that requires RSR just do rotation and not targetting.")]
        Henched,
    }

    /// <summary>
    /// Hostile target.
    /// </summary>
    public enum TargetHostileType : byte
    {
        /// <summary>
        /// All targets that are in range for any abilities (Tanks/Autoduty).
        /// </summary>
        [Description("All targets that are in range for any abilities (Tanks/Autoduty)")]
        AllTargetsCanAttack,

        /// <summary>
        /// Previously engaged targets (Non-Tanks).
        /// </summary>
        [Description("Previously engaged targets (Non-Tanks)")]
        TargetsHaveTarget,

        /// <summary>
        /// All targets when solo in duty, or previously engaged.
        /// </summary>
        [Description("All targets when solo in duty (includes Occult Crescent), or previously engaged.")]
        AllTargetsWhenSoloInDuty,

        /// <summary>
        /// All targets when solo, or previously engaged.
        /// </summary>
        [Description("All targets when solo, or previously engaged.")]
        AllTargetsWhenSolo
    }
}
