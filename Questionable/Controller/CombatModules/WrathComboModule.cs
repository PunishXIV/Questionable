using System;
using System.Diagnostics.CodeAnalysis;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps;

namespace Questionable.Controller.CombatModules;

internal sealed class WrathComboModule : ICombatModule, IDisposable
{
    private const string CallbackPrefix = "Questionable$Wrath";

    private readonly ILogger<WrathComboModule> _logger;
    private readonly Configuration _configuration;
    private readonly ICallGateSubscriber<object> _test;
    private readonly ICallGateSubscriber<string, string, string, Guid?> _registerForLeaseWithCallback;
    private readonly ICallGateSubscriber<Guid, object> _releaseControl;
    private readonly ICallGateSubscriber<Guid, bool, ESetResult> _setAutoRotationState;
    private readonly ICallGateSubscriber<Guid, object, object, ESetResult> _setAutoRotationConfigState;
    private readonly ICallGateSubscriber<Guid, ESetResult> _setCurrentJobAutoRotationReady;
    private readonly ICallGateProvider<int, string, object> _callback;

    private Guid? _lease;

    public WrathComboModule(ILogger<WrathComboModule> logger, Configuration configuration,
        IDalamudPluginInterface pluginInterface)
    {
        _logger = logger;
        _configuration = configuration;
        _test = pluginInterface.GetIpcSubscriber<object>("WrathCombo.Test");
        _registerForLeaseWithCallback =
            pluginInterface.GetIpcSubscriber<string, string, string, Guid?>("WrathCombo.RegisterForLeaseWithCallback");
        _releaseControl = pluginInterface.GetIpcSubscriber<Guid, object>("WrathCombo.ReleaseControl");
        _setAutoRotationState = pluginInterface.GetIpcSubscriber<Guid, bool, ESetResult>("WrathCombo.SetAutoRotationState");
        _setAutoRotationConfigState = pluginInterface.GetIpcSubscriber<Guid, object, object, ESetResult>("WrathCombo.SetAutoRotationConfigState");
        _setCurrentJobAutoRotationReady =
            pluginInterface.GetIpcSubscriber<Guid, ESetResult>("WrathCombo.SetCurrentJobAutoRotationReady");

        _callback = pluginInterface.GetIpcProvider<int, string, object>($"{CallbackPrefix}.WrathComboCallback");
        _callback.RegisterAction(Callback);
    }

    public bool CanHandleFight(CombatController.CombatData combatData)
    {
        if (_configuration.General.CombatModule != Configuration.ECombatModule.WrathCombo)
            return false;

        try
        {
            _test.InvokeAction();
            return true;
        }
        catch (IpcError)
        {
            return false;
        }
    }

    public bool Start(CombatController.CombatData combatData)
    {
        try
        {
            _lease = _registerForLeaseWithCallback.InvokeFunc("Questionable", "Questionable", CallbackPrefix);
            if (_lease != null)
            {
                _logger.LogDebug("Wrath combo lease: {Lease}", _lease.Value);

                ESetResult autoRotationSet = _setAutoRotationState.InvokeFunc(_lease.Value, true);
                if (!autoRotationSet.IsSuccess())
                {
                    _logger.LogError("Unable to set autorotation state");
                    Stop();
                    return false;
                }

                ESetResult currentJobSetForAutoRotation = _setCurrentJobAutoRotationReady.InvokeFunc(_lease.Value);
                if (!currentJobSetForAutoRotation.IsSuccess())
                {
                    _logger.LogError("Unable to set current job for autorotation");
                    Stop();
                    return false;
                }

                ESetResult healerRotationModeSet = _setAutoRotationConfigState.InvokeFunc(_lease.Value,
                    AutoRotationConfigOption.HealerRotationMode, HealerRotationMode.Lowest_Current);
                if (!healerRotationModeSet.IsSuccess())
                {
                    _logger.LogError("Unable to configure healing priority for autorotation: {Result}",
                        healerRotationModeSet);
                }

                return true;
            }
            else
            {
                _logger.LogError("Wrath combo did not return a lease");
                return false;
            }
        }
        catch (IpcError e)
        {
            _logger.LogError(e, "Unable to use wrath combo for combat");
            return false;
        }
    }

    public bool Stop()
    {
        try
        {
            if (_lease != null)
            {
                _releaseControl.InvokeAction(_lease.Value);
                _lease = null;
            }

            return true;
        }
        catch (IpcError e)
        {
            _logger.LogWarning(e, "Could not turn off wrath combo");
            return false;
        }
    }

    public void Update(IGameObject nextTarget)
    {
        if (_lease == null)
            throw new TaskException("Wrath Combo Lease is cancelled");
    }

    public bool CanAttack(IBattleNpc target) => true;

    private void Callback(int reason, string additionalInfo)
    {
        _logger.LogWarning("WrathCombo callback: {Reason} ({Info})", reason, additionalInfo);
        _lease = null;
    }

    public void Dispose()
    {
        Stop();
        _callback.UnregisterAction();
    }

    [PublicAPI]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public enum ESetResult
    {
        Okay = 0,
        OkayWorking = 1,

        IpcDisabled = 10,
        InvalidLease = 11,
        BlacklistedLease = 12,
        Duplicate = 13,
        PlayerNotAvailable = 14,
        InvalidConfiguration = 15,
        InvalidValue = 16,
    }

    [PublicAPI]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public enum AutoRotationConfigOption
    {
        InCombatOnly = 0,
        DPSRotationMode = 1,
        HealerRotationMode = 2,
        FATEPriority = 3,
        QuestPriority = 4,
        SingleTargetHPP = 5,
        AoETargetHPP = 6,
        SingleTargetRegenHPP = 7,
        ManageKardia = 8,
        AutoRez = 9,
        AutoRezDPSJobs = 10,
        AutoCleanse = 11,
        IncludeNPCs = 12,
        OnlyAttackInCombat = 13,
        OrbwalkerIntegration = 14,
        AutoRezOutOfParty = 15,
        DPSAoETargets = 16,
        SingleTargetExcogHPP = 17,
    }

    [PublicAPI]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public enum HealerRotationMode
    {
        Manual = 0,
        Highest_Current = 1,
        Lowest_Current = 2,
    }
}

internal static class WrathResultExtensions
{
    public static bool IsSuccess(this WrathComboModule.ESetResult result)
    {
        return result is WrathComboModule.ESetResult.Okay or WrathComboModule.ESetResult.OkayWorking;
    }
}
