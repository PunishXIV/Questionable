using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps;
using Questionable.Data;
using Questionable.Model.Questing;
namespace Questionable.External;

internal sealed class AutoDutyIpc
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    TerritoryData territoryData,
    ILogger<AutoDutyIpc> logger)
{
    public enum DutyMode
    {
        Support = 1,
        UnsyncRegular = 2
    }

    private readonly Configuration _configuration = configuration;
    private readonly ICallGateSubscriber<uint, bool> _contentHasPath = pluginInterface.GetIpcSubscriber<uint, bool>("AutoDuty.ContentHasPath");
    private readonly ICallGateSubscriber<bool> _isStopped = pluginInterface.GetIpcSubscriber<bool>("AutoDuty.IsStopped");
    private readonly ILogger<AutoDutyIpc> _logger = logger;
    private readonly ICallGateSubscriber<uint, int, bool, object> _run = pluginInterface.GetIpcSubscriber<uint, int, bool, object>("AutoDuty.Run");
    private readonly ICallGateSubscriber<string, string, object> _setConfig = pluginInterface.GetIpcSubscriber<string, string, object>("AutoDuty.SetConfig");
    private readonly ICallGateSubscriber<object> _stop = pluginInterface.GetIpcSubscriber<object>("AutoDuty.Stop");
    private readonly TerritoryData _territoryData = territoryData;

    public bool IsConfiguredToRunContent(DutyOptions? dutyOptions)
    {
        if (dutyOptions == null || dutyOptions.ContentFinderConditionId == 0)
            return false;

        if (!_configuration.Duties.RunInstancedContentWithAutoDuty)
            return false;

        if (_configuration.Duties.BlacklistedDutyCfcIds.Contains(dutyOptions.ContentFinderConditionId))
            return false;

        if (_configuration.Duties.WhitelistedDutyCfcIds.Contains(dutyOptions.ContentFinderConditionId) &&
            _territoryData.TryGetContentFinderCondition(dutyOptions.ContentFinderConditionId, out TerritoryData.ContentFinderConditionData? _))
        {
            return true;
        }

        return dutyOptions.Enabled && HasPath(dutyOptions.ContentFinderConditionId);
    }

    public bool HasPath(uint cfcId)
    {
        if (!_territoryData.TryGetContentFinderCondition(cfcId, out TerritoryData.ContentFinderConditionData? cfcData))
            return false;

        return IpcInvoke.SafeFunc(() => _contentHasPath.InvokeFunc(cfcData.TerritoryId), false,
            _logger, "Unable to query AutoDuty for path in territory {TerritoryType}", cfcData.TerritoryId);
    }

    public void StartInstance(uint cfcId, DutyMode dutyMode)
    {
        if (!_territoryData.TryGetContentFinderCondition(cfcId, out TerritoryData.ContentFinderConditionData? cfcData))
            throw new TaskException($"Unknown ContentFinderConditionId {cfcId}");

        try
        {
            _setConfig.InvokeAction("Unsynced", $"{dutyMode == DutyMode.UnsyncRegular}");
            _setConfig.InvokeAction("dutyModeEnum", dutyMode switch
            {
                DutyMode.Support => "Support",
                DutyMode.UnsyncRegular => "Regular",
                var _ => throw new ArgumentOutOfRangeException(nameof(dutyMode), dutyMode, null)
            });

            _run.InvokeAction(cfcData.TerritoryId, 1, !_configuration.Advanced.DisableAutoDutyBareMode);
        }
        catch (IpcError e)
        {
            throw new TaskException($"Unable to run content with AutoDuty: {e.Message}", e);
        }
    }

    public bool IsStopped() => IpcInvoke.SafeFunc(() => _isStopped.InvokeFunc(), true);

    public void Stop()
    {
        try
        {
            _logger.LogInformation("Calling AutoDuty.Stop");
            _stop.InvokeAction();
        }
        catch (IpcError e)
        {
            throw new TaskException($"Unable to stop AutoDuty: {e.Message}", e);
        }
    }
}
