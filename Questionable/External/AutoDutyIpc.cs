using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps;
using Questionable.Data;
using Questionable.Model.Questing;

namespace Questionable.External;

internal sealed class AutoDutyIpc
{
    private readonly Configuration _configuration;
    private readonly TerritoryData _territoryData;
    private readonly ILogger<AutoDutyIpc> _logger;
    private readonly ICallGateSubscriber<uint, bool> _contentHasPath;
    private readonly ICallGateSubscriber<string, string, object> _setConfig;
    private readonly ICallGateSubscriber<uint, int, bool, object> _run;
    private readonly ICallGateSubscriber<bool> _isStopped;
    private readonly ICallGateSubscriber<object> _stop;

    public AutoDutyIpc(IDalamudPluginInterface pluginInterface, Configuration configuration,
        TerritoryData territoryData, ILogger<AutoDutyIpc> logger)
    {
        _configuration = configuration;
        _territoryData = territoryData;
        _logger = logger;
        _contentHasPath = pluginInterface.GetIpcSubscriber<uint, bool>("AutoDuty.ContentHasPath");
        _setConfig = pluginInterface.GetIpcSubscriber<string, string, object>("AutoDuty.SetConfig");
        _run = pluginInterface.GetIpcSubscriber<uint, int, bool, object>("AutoDuty.Run");
        _isStopped = pluginInterface.GetIpcSubscriber<bool>("AutoDuty.IsStopped");
        _stop = pluginInterface.GetIpcSubscriber<object>("AutoDuty.Stop");
    }

    public bool IsConfiguredToRunContent(DutyOptions? dutyOptions)
    {
        if (dutyOptions == null || dutyOptions.ContentFinderConditionId == 0)
            return false;

        if (!_configuration.Duties.RunInstancedContentWithAutoDuty)
            return false;

        if (_configuration.Duties.BlacklistedDutyCfcIds.Contains(dutyOptions.ContentFinderConditionId))
            return false;

        if (_configuration.Duties.WhitelistedDutyCfcIds.Contains(dutyOptions.ContentFinderConditionId) &&
            _territoryData.TryGetContentFinderCondition(dutyOptions.ContentFinderConditionId, out _))
            return true;

        return dutyOptions.Enabled && HasPath(dutyOptions.ContentFinderConditionId);
    }

    public bool HasPath(uint cfcId)
    {
        if (!_territoryData.TryGetContentFinderCondition(cfcId, out var cfcData))
            return false;

        try
        {
            return _contentHasPath.InvokeFunc(cfcData.TerritoryId);
        }
        catch (IpcError e)
        {
            _logger.LogWarning("Unable to query AutoDuty for path in territory {TerritoryType}: {Message}",
                cfcData.TerritoryId, e.Message);
            return false;
        }
    }

    public void StartInstance(uint cfcId)
    {
        if (!_territoryData.TryGetContentFinderCondition(cfcId, out var cfcData))
            throw new TaskException($"Unknown ContentFinderConditionId {cfcId}");

        try
        {
            _setConfig.InvokeAction("dutyModeEnum", "Support");
            _run.InvokeAction(cfcData.TerritoryId, 1, !_configuration.Advanced.DisableAutoDutyBareMode);
        }
        catch (IpcError e)
        {
            throw new TaskException($"Unable to run content with AutoDuty: {e.Message}", e);
        }
    }

    public bool IsStopped()
    {
        try
        {
            return _isStopped.InvokeFunc();
        }
        catch (IpcError)
        {
            return true;
        }
    }

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
