using System;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.EzIpcManager;
using ECommons.Reflection;
using Microsoft.Extensions.Logging;
using Questionable.Controller;
using Questionable.Data;
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
        _logger.LogInformation($"Enabled:{_wasEnabled}");

        _framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        IPCSubscriber_Common.DisposeAll(_disposalTokens);
    }

    private void OnUpdate(IFramework framework)
    {
        if (IPCSubscriber_Common.IsReady("YesAlready"))
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

    internal sealed class IPCSubscriber_Common
    {
        internal static bool IsReady(string pluginName)
        {
            return DalamudReflector.TryGetDalamudPlugin(pluginName, out object _, false, true);
        }

        internal static Version? Version(string pluginName)
        {
            Version _version;
            if (DalamudReflector.TryGetDalamudPlugin(pluginName, out object? dalamudPlugin, false, true))
                _version = dalamudPlugin.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            else
                _version = new(0, 0, 0, 0);
            return _version;
        }

        internal static void DisposeAll(EzIPCDisposalToken[] _disposalTokens)
        {
            foreach (EzIPCDisposalToken token in _disposalTokens)
            {
                try
                {
                    token.Dispose();
                }
                catch (Exception ex)
                {
                    Svc.Log.Error($"Error while unregistering IPC: {ex}");
                }
            }
        }
    }
}
