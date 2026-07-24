using System.ComponentModel;
using Dalamud.Plugin.Ipc;
using Questionable.Model.Common;

namespace Questionable.External;

internal sealed class RotationSolverRebornIpc(IDalamudPluginInterface pluginInterface, Configuration configuration, ILogger<RotationSolverRebornIpc> logger)
{
    private readonly ICallGateSubscriber<StateCommandType, object> ChangeOperatingMode =
        pluginInterface.GetIpcSubscriber<StateCommandType, object>("RotationSolverReborn.ChangeOperatingMode");
    private readonly ICallGateSubscriber<string, object> Test =
        pluginInterface.GetIpcSubscriber<string, object>("RotationSolverReborn.Test");
    public bool IsEnabled => IsReady("RotationSolver");

    public bool RotationAuto()
    {
        if (configuration.General.CombatModule != ECombatModule.RotationSolverReborn)
            return false;
        _ = IpcInvoke.SafeFunc(() => { ChangeOperatingMode.InvokeAction(StateCommandType.Henched); return true; }, fallback: false);
        return true;
    }

    public void RotationStop()
    {
        if (configuration.General.CombatModule != ECombatModule.RotationSolverReborn)
            return;
        _ = IpcInvoke.SafeFunc(() => { ChangeOperatingMode.InvokeAction(StateCommandType.Off); return true; }, fallback: false);
    }

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

        /// <summary>
        /// 
        /// </summary>
        [Description("This mode is managed by the AutoDuty plugin")]
        AutoDuty,

        /// <summary>
        /// 
        /// </summary>
        [Description("This mode is managed by the Henchman plugin, or any other plugin that requires RSR just do rotation and not targetting.")]
        Henched,
    }

    /// <summary>
    /// Some Other Commands.
    /// </summary>
    public enum OtherCommandType : byte
    {
        /// <summary>
        /// Open the settings.
        /// </summary>
        [Description("Open the settings.")]
        Settings,

        /// <summary>
        /// Open the rotations.
        /// </summary>
        [Description("Open the rotations.")]
        Rotations,

        /// <summary>
        /// Open the rotations.
        /// </summary>
        [Description("Open the duty rotations.")]
        DutyRotations,

        /// <summary>
        /// Perform the actions.
        /// </summary>
        [Description("Perform the actions.")]
        DoActions,

        /// <summary>
        /// Toggle the actions.
        /// </summary>
        [Description("Toggle the actions.")]
        ToggleActions,

        /// <summary>
        /// Do the next action.
        /// </summary>
        [Description("Do the next action.")]
        NextAction,
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

    public static string GetHostileTypeDescription(TargetHostileType type)
    {
        return type switch
        {
            TargetHostileType.AllTargetsCanAttack => "All Targets Can Attack aka Tank/Autoduty Mode",
            TargetHostileType.TargetsHaveTarget => "Targets Have A Target",
            TargetHostileType.AllTargetsWhenSoloInDuty => "All Targets When Solo In Duty",
            TargetHostileType.AllTargetsWhenSolo => "All Targets When Solo",
            _ => "Unknown Target Type"
        };
    }

    /// <summary>
    /// The type of targeting.
    /// </summary>
    public enum TargetingType
    {
        /// <summary>
        /// Find the target whose hit box is biggest.
        /// </summary>
        [Description("Big")]
        Big,

        /// <summary>
        /// Find the target whose hit box is smallest.
        /// </summary>
        [Description("Small")]
        Small,

        /// <summary>
        /// Find the target whose HP is highest.
        /// </summary>
        [Description("High HP")]
        HighHP,

        /// <summary>
        /// Find the target whose HP is lowest.
        /// </summary>
        [Description("Low HP")]
        LowHP,

        /// <summary>
        /// Find the target whose HP percentage is highest.
        /// </summary>
        [Description("High HP%")]
        HighHPPercent,

        /// <summary>
        /// Find the target whose HP percentage is lowest.
        /// </summary>
        [Description("Low HP%")]
        LowHPPercent,

        /// <summary>
        /// Find the target whose max HP is highest.
        /// </summary>
        [Description("High Max HP")]
        HighMaxHP,

        /// <summary>
        /// Find the target whose max HP is lowest.
        /// </summary>
        [Description("Low Max HP")]
        LowMaxHP,

        /// <summary>
        /// Find the target that is nearest.
        /// </summary>
        [Description("Nearest")]
        Nearest,

        /// <summary>
        /// Find the target that is farthest.
        /// </summary>
        [Description("Farthest")]
        Farthest,
    }
}
