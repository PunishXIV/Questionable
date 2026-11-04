using System.ComponentModel;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc;
using Questionable.Model.Common;
using WrathCombo.API;
using WrathCombo.API.Enum;

namespace Questionable.Controller.CombatModules;

[RegisterSingleton<ICombatModule, WrathComboModule>(Duplicate = DuplicateStrategy.Append)]
internal sealed class WrathComboModule : ICombatModule, IDisposable
{
    private const string CallbackPrefix = "Questionable$Wrath";
    private readonly ICallGateProvider<int, string, object> _callback;
    private readonly Configuration _configuration;

    private readonly IFramework _framework;
    private readonly ILogger<WrathComboModule> _logger;

    private Guid? _lease;
    private bool _cleanupFailureLogged;

    public WrathComboModule(ILogger<WrathComboModule> logger,
        Configuration configuration,
        IDalamudPluginInterface pluginInterface,
        IFramework framework)
    {
        _logger = logger;
        _configuration = configuration;
        _framework = framework;

        _callback =
            pluginInterface.GetIpcProvider<int, string, object>(
                $"{CallbackPrefix}.WrathComboCallback");
        _callback.RegisterAction(Callback);
    }

    public bool CanHandleFight(CombatController.CombatData combatData)
    {
        if (_configuration.General.CombatModule !=
            ECombatModule.WrathCombo)
            return false;

        try
        {
            return WrathIPCWrapper.IPCReady();
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "WrathCombo is unavailable");
            return false;
        }
    }

    public bool Start(CombatController.CombatData combatData)
    {
        if (_lease != null)
            return false;

        _cleanupFailureLogged = false;
        bool started = false;
        try
        {
            if (!WrathIPCWrapper.IPCReady())
                return false;
            _lease = WrathIPCWrapper.RegisterForLeaseWithCallback(
                "Questionable",
                "Questionable",
                CallbackPrefix);

            if (!_lease.HasValue)
            {
                _logger.LogError("Problem with WrathCombo leasing. " +
                                 "Please report to Questionable or Wrath team.");
                return false;
            }

            SetResult autoRotationSet = WrathIPCWrapper.SetAutoRotationState(_lease.Value, true);
            if (!autoRotationSet.IsSuccess())
            {
                _logger.LogError("Unable to set Wrath's Auto Rotation state");
                return false;
            }

            SetResult currentJobSetForAutoRotation = WrathIPCWrapper.SetCurrentJobAutoRotationReady(_lease.Value);
            if (!currentJobSetForAutoRotation.IsSuccess())
            {
                _logger.LogError("Unable to set Wrath to be Auto Rotation-ready");
                return false;
            }

            // Make Wrath Work
            SetResult targetingMode = WrathIPCWrapper.SetAutoRotationConfigState(_lease.Value,
                AutoRotationConfigOption.DPSRotationMode,
                DPSRotationMode.Manual);
            SetResult healerRotationMode = WrathIPCWrapper.SetAutoRotationConfigState(_lease.Value,
                AutoRotationConfigOption.HealerRotationMode,
                HealerRotationMode.Lowest_Current);
            SetResult healerMagicTargeting = WrathIPCWrapper.SetAutoRotationConfigState(_lease.Value,
                AutoRotationConfigOption.HealerAlwaysHardTarget, false);
            SetResult combatOnly = WrathIPCWrapper.SetAutoRotationConfigState(_lease.Value,
                AutoRotationConfigOption.InCombatOnly, false);

            // Make Wrath Work well
            SetResult includeNPCs = WrathIPCWrapper.SetAutoRotationConfigState(_lease.Value,
                AutoRotationConfigOption.IncludeNPCs, true);
            SetResult targetCombatOnly = WrathIPCWrapper.SetAutoRotationConfigState(_lease.Value,
                AutoRotationConfigOption.OnlyAttackInCombat, false);
            SetResult cleanse = WrathIPCWrapper.SetAutoRotationConfigState(_lease.Value,
                AutoRotationConfigOption.AutoCleanse, true);

            // Nice-to-haves
            SetResult rez = WrathIPCWrapper.SetAutoRotationConfigState(_lease.Value,
                AutoRotationConfigOption.AutoRez, true);
            SetResult rezAsDPS = WrathIPCWrapper.SetAutoRotationConfigState(_lease.Value,
                AutoRotationConfigOption.AutoRezDPSJobs, true);
            SetResult kardia = WrathIPCWrapper.SetAutoRotationConfigState(_lease.Value,
                AutoRotationConfigOption.ManageKardia, true);
            SetResult aoeTargetThreshold = WrathIPCWrapper.SetAutoRotationConfigState(_lease.Value,
                AutoRotationConfigOption.DPSAoETargets,
                3);
            SetResult rezNonParty = WrathIPCWrapper.SetAutoRotationConfigState(_lease.Value,
                AutoRotationConfigOption.AutoRezOutOfParty, false);

            if (!WrathResultExtensions.AllSuccessful(out string failed,
                ("HealerRotationMode", healerRotationMode),
                ("DPSRotationMode", targetingMode),
                ("InCombatOnly", combatOnly),
                ("IncludeNPCs", includeNPCs),
                ("OnlyAttackInCombat", targetCombatOnly),
                ("AutoRez", rez),
                ("AutoRezDPSJobs", rezAsDPS),
                ("AutoCleanse", cleanse),
                ("HealerAlwaysHardTarget", healerMagicTargeting),
                ("ManageKardia", kardia),
                ("DPSAoETargets", aoeTargetThreshold),
                ("AutoRezOutOfParty", rezNonParty)))
            {
                _logger.LogError("Unable to configure Wrath Auto Rotation " +
                                 "settings: {Result}",
                    string.Join(", ", failed));
                return false;
            }

            VerifyActive();
            started = true;
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not configure WrathCombo combat");
            return false;
        }
        finally
        {
            if (!started)
                Stop();
        }
    }

    public bool Pause() => SetRotationEnabled(false);

    public bool Resume() => SetRotationEnabled(true);

    private bool SetRotationEnabled(bool enabled)
    {
        try
        {
            VerifyActive();
            if (!WrathIPCWrapper.SetAutoRotationState(_lease!.Value, enabled).IsSuccess())
                return false;
            VerifyActive();
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not change WrathCombo rotation for combat item use");
            return false;
        }
    }

    public bool Stop()
    {
        try
        {
            if (_lease is { } lease)
            {
                WrathIPCWrapper.ReleaseControl(lease);
                _lease = null;
            }
            _cleanupFailureLogged = false;
            return true;
        }
        catch (Exception e)
        {
            if (!_cleanupFailureLogged)
            {
                _logger.LogWarning(e, "Could not release WrathCombo lease; retaining it for retry");
                _cleanupFailureLogged = true;
            }
            return false;
        }
    }

    public void VerifyActive()
    {
        if (_lease == null)
            throw new TaskException("WrathCombo lease was cancelled.");
        try
        {
            if (!WrathIPCWrapper.IPCReady())
                throw new TaskException("WrathCombo is no longer ready.");
        }
        catch (Exception e) when (e is not TaskException)
        {
            throw new TaskException("WrathCombo is no longer available.", e);
        }
    }

    public void Update(IGameObject nextTarget) => VerifyActive();

    public bool CanAttack(IBattleNpc target) => true;

    public void Dispose()
    {
        IpcInvoke.TryOnFrameworkThread(_framework, () => Stop(), _logger);
        _callback.UnregisterAction();
    }

    private void Callback(int reason, string additionalInfo)
    {
        CancellationReason realReason = (CancellationReason)reason;
        string description = (typeof(CancellationReason).GetField(realReason.ToString())?.GetCustomAttribute<DescriptionAttribute>())?.Description ?? string.Empty;
        _logger.LogWarning("WrathCombo IPC Lease Cancelled: {ReasonDescription} " +
                           "({Reason}; for: {Info})",
            description, realReason.ToString(), additionalInfo);
        _lease = null;
    }
}
