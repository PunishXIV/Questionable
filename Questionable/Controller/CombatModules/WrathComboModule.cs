using System;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps;
using WrathCombo.API;
using WrathCombo.API.Enum;
using WrathError = WrathCombo.API.Error;

namespace Questionable.Controller.CombatModules;

internal sealed class WrathComboModule : ICombatModule, IDisposable
{
    private const string CallbackPrefix = "Questionable$Wrath";
    private readonly ICallGateProvider<int, string, object> _callback;
    private readonly Configuration _configuration;

    private readonly ILogger<WrathComboModule> _logger;

    private Guid? _lease;

    public WrathComboModule(ILogger<WrathComboModule> logger,
        Configuration configuration,
        IDalamudPluginInterface pluginInterface)
    {
        _logger = logger;
        _configuration = configuration;

        _callback =
            pluginInterface.GetIpcProvider<int, string, object>(
                $"{CallbackPrefix}.WrathComboCallback");
        _callback.RegisterAction(Callback);
    }

    public bool CanHandleFight(CombatController.CombatData combatData)
    {
        if (_configuration.General.CombatModule !=
            Configuration.ECombatModule.WrathCombo)
            return false;

        try
        {
            WrathIPCWrapper.Test();
            if (!WrathIPCWrapper.IPCReady())
                throw new EvaluateException("WrathCombo IPC not ready");
            return true;
        }
        catch (WrathError.Exception e) when (e is WrathError.APIBehindException or
            WrathError.UninitializedException)
        {
            _logger.LogWarning(e, "Problem with WrathCombo.API usage. " +
                                  "Please report to Questionable or Wrath team.");
        }
        catch (EvaluateException e)
        {
            _logger.LogWarning(e, "Problem with WrathCombo usage. " +
                                  "Please report to Wrath team.");
        }
        catch (Exception)
        {
            // Ignore
        }

        return false;
    }

    public bool Start(CombatController.CombatData combatData)
    {
        try
        {
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

            SetResult autoRotationSet = WrathIPCWrapper
                .SetAutoRotationState(_lease.Value);
            if (!autoRotationSet.IsSuccess())
            {
                _logger.LogError("Unable to set Wrath's Auto Rotation state");
                Stop();
                return false;
            }

            SetResult currentJobSetForAutoRotation = WrathIPCWrapper
                .SetCurrentJobAutoRotationReady(_lease.Value);
            if (!currentJobSetForAutoRotation.IsSuccess())
            {
                _logger.LogError("Unable to set Wrath to be Auto Rotation-ready");
                Stop();
                return false;
            }

            // Make Wrath Work
            SetResult targetingMode = WrathIPCWrapper
                .SetAutoRotationConfigState(_lease.Value,
                    AutoRotationConfigOption.DPSRotationMode,
                    DPSRotationMode.Manual);
            SetResult healerRotationMode = WrathIPCWrapper
                .SetAutoRotationConfigState(_lease.Value,
                    AutoRotationConfigOption.HealerRotationMode,
                    HealerRotationMode.Lowest_Current);
            SetResult healerMagicTargeting = WrathIPCWrapper
                .SetAutoRotationConfigState(_lease.Value,
                    AutoRotationConfigOption.HealerAlwaysHardTarget, value: false);
            SetResult combatOnly = WrathIPCWrapper
                .SetAutoRotationConfigState(_lease.Value,
                    AutoRotationConfigOption.InCombatOnly, value: false);

            // Make Wrath Work well
            SetResult includeNPCs = WrathIPCWrapper
                .SetAutoRotationConfigState(_lease.Value,
                    AutoRotationConfigOption.IncludeNPCs, value: true);
            SetResult targetCombatOnly = WrathIPCWrapper
                .SetAutoRotationConfigState(_lease.Value,
                    AutoRotationConfigOption.OnlyAttackInCombat, value: false);
            SetResult cleanse = WrathIPCWrapper
                .SetAutoRotationConfigState(_lease.Value,
                    AutoRotationConfigOption.AutoCleanse, value: true);

            // Nice-to-haves
            SetResult rez = WrathIPCWrapper
                .SetAutoRotationConfigState(_lease.Value,
                    AutoRotationConfigOption.AutoRez, value: true);
            SetResult rezAsDPS = WrathIPCWrapper
                .SetAutoRotationConfigState(_lease.Value,
                    AutoRotationConfigOption.AutoRezDPSJobs, value: true);
            SetResult kardia = WrathIPCWrapper
                .SetAutoRotationConfigState(_lease.Value,
                    AutoRotationConfigOption.ManageKardia, value: true);
            SetResult aoeTargetThreshold = WrathIPCWrapper
                .SetAutoRotationConfigState(_lease.Value,
                    AutoRotationConfigOption.DPSAoETargets,
                    3);
            SetResult rezNonParty = WrathIPCWrapper
                .SetAutoRotationConfigState(_lease.Value,
                    AutoRotationConfigOption.AutoRezOutOfParty, value: false);

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
                Stop();
            }

            return true;
        }
        catch (WrathError.IPCException e)
        {
            _logger.LogWarning(e, "Problem with Wrath Combo Setup. " +
                                  "Please report to Wrath team.");
        }
        catch (Exception)
        {
            // Ignore
        }

        return false;
    }

    public bool Stop()
    {
        try
        {
            if (_lease != null)
                WrathIPCWrapper.ReleaseControl(_lease.Value);

            return true;
        }
        catch (WrathError.IPCException e)
        {
            _logger.LogWarning(e, "Problem with Wrath Combo stopping. " +
                                  "Please report to Wrath team.");
        }
        catch (Exception)
        {
            // Ignore
        }
        finally
        {
            _lease = null;
        }

        return false;
    }

    public void Update(IGameObject nextTarget)
    {
        if (_lease == null)
            throw new TaskException("Wrath Combo Lease is cancelled");
    }

    public bool CanAttack(IBattleNpc target) => true;

    public void Dispose()
    {
        Stop();
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

internal static class WrathResultExtensions
{
    public static bool AllSuccessful
    (out string failedVariableNames,
        params (string name, SetResult result)[] results)
    {
        string[] failed = results
            .Where(r => !r.result.IsSuccess())
            .Select(r => r.name)
            .ToArray();

        failedVariableNames = string.Join(", ", failed);
        return failed.Length == 0;
    }

    public static bool IsSuccess(this SetResult result) => result is SetResult.Okay or SetResult.OkayWorking;
}
