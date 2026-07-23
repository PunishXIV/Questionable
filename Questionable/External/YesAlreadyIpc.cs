using System;
using Dalamud.Plugin.Services;
using ECommons.EzIpcManager;
using Microsoft.Extensions.Logging;
using Questionable.Controller;
using Questionable.Data;
using static Questionable.External.IPCUtils;
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
namespace Questionable.External;

internal sealed class YesAlreadyIpc : IDisposable
{
    private static readonly EzIPCDisposalToken[] _disposalTokens = EzIPC.Init(typeof(YesAlreadyIpc), "YesAlready", SafeWrapper.IPCException);
    [EzIPC("IsPluginEnabled")] public static readonly Func<bool> IsPluginEnabled;
    [EzIPC("SetPluginEnabled")] private static readonly Action<bool> SetPluginEnabled;
    private readonly IClientState _clientState;

    private readonly IFramework _framework;
    private readonly ILogger<YesAlreadyIpc> _logger;
    private readonly QuestController _questController;
    private readonly TerritoryData _territoryData;

    private bool _wasEnabled;

    public YesAlreadyIpc(IFramework framework,
        QuestController questController,
        TerritoryData territoryData,
        IClientState clientState,
        ILogger<YesAlreadyIpc> logger)
    {
        _framework = framework;
        _questController = questController;
        _territoryData = territoryData;
        _clientState = clientState;
        _logger = logger;
        _wasEnabled = IsPluginEnabled();
        _logger.LogInformation("Enabled:{WasEnabled}", _wasEnabled);

        _framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        if (IPCSubscriber.IsInstalled("YesAlready") && _wasEnabled && !IsPluginEnabled())
        {
            _logger.LogDebug("Re-enabling YesAlready on dispose");
            SetPluginEnabled(true);
        }

        IPCSubscriber.DisposeAll(_disposalTokens);
    }

    private void OnUpdate(IFramework framework)
    {
        if (IPCSubscriber.IsInstalled("YesAlready"))
        {
            bool hasActiveQuest = (_questController.IsRunning ||
                                   _questController.AutomationType != QuestController.EAutomationType.Manual) &&
                                  !_territoryData.IsDutyInstance(_clientState.TerritoryType);
            if (hasActiveQuest)
            {
                if (IsPluginEnabled() && !_wasEnabled)
                {
                    _logger.LogDebug("Requested YesAlready off");
                    SetPluginEnabled(false);
                    _wasEnabled = true;
                }
            }
            else
            {
                if (!IsPluginEnabled() && _wasEnabled)
                {
                    _logger.LogDebug("Requested YesAlready on");
                    SetPluginEnabled(true);
                    _wasEnabled = false;
                }
            }
        }
    }
}
