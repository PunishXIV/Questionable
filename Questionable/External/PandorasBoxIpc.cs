using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Questionable.Controller;
using Questionable.Data;

namespace Questionable.External;

internal sealed class PandorasBoxIpc : IDisposable
{
    private static readonly ImmutableHashSet<string> ConflictingFeatures = new HashSet<string>
    {
        // Actions
        "Auto-Meditation",
        "Auto-Motif (Out of Combat)",
        "Auto-Mount after Combat",
        "Auto-Mount after Gathering",
        "Auto-Peleton",
        "Auto-Spring in Sanctuaries",

        // Targets
        "Auto-interact with Gathering Nodes",

        // Other
        "Pandora Quick Gather",
    }.ToImmutableHashSet();

    private readonly IFramework _framework;
    private readonly QuestController _questController;
    private readonly TerritoryData _territoryData;
    private readonly IClientState _clientState;
    private readonly ILogger<PandorasBoxIpc> _logger;

    private readonly ICallGateSubscriber<string, bool?> _getFeatureEnabled;
    private readonly ICallGateSubscriber<string, bool, object?> _setFeatureEnabled;

    private bool _loggedIpcError;
    private HashSet<string>? _pausedFeatures;

    public PandorasBoxIpc(IDalamudPluginInterface pluginInterface,
        IFramework framework,
        QuestController questController,
        TerritoryData territoryData,
        IClientState clientState,
        ILogger<PandorasBoxIpc> logger)
    {
        _framework = framework;
        _questController = questController;
        _territoryData = territoryData;
        _clientState = clientState;
        _logger = logger;
        _getFeatureEnabled = pluginInterface.GetIpcSubscriber<string, bool?>("PandorasBox.GetFeatureEnabled");
        _setFeatureEnabled = pluginInterface.GetIpcSubscriber<string, bool, object?>("PandorasBox.SetFeatureEnabled");
        logger.LogInformation("Pandora's Box auto active time maneuver enabled: {IsAtmEnabled}",
            IsAutoActiveTimeManeuverEnabled);

        _framework.Update += OnUpdate;
    }

    public bool IsAutoActiveTimeManeuverEnabled
    {
        get
        {
            try
            {
                return _getFeatureEnabled.InvokeFunc("Auto Active Time Maneuver") == true;
            }
            catch (IpcError e)
            {
                if (!_loggedIpcError)
                {
                    _loggedIpcError = true;
                    _logger.LogWarning(e, "Could not query pandora's box for feature status, probably not installed");
                }

                return false;
            }
        }
    }

    private void OnUpdate(IFramework framework)
    {
        bool hasActiveQuest = _questController.IsRunning ||
                              _questController.AutomationType != QuestController.EAutomationType.Manual;
        if (hasActiveQuest && !_territoryData.IsDutyInstance(_clientState.TerritoryType))
        {
            DisableConflictingFeatures();
        }
        else
        {
            RestoreConflictingFeatures();
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        RestoreConflictingFeatures();
    }

    private void DisableConflictingFeatures()
    {
        if (_pausedFeatures != null)
            return;

        _pausedFeatures = new HashSet<string>();

        foreach (var feature in ConflictingFeatures)
        {
            try
            {
                bool? isEnabled = _getFeatureEnabled.InvokeFunc(feature);
                if (isEnabled == true)
                {
                    _setFeatureEnabled.InvokeAction(feature, false);
                    _pausedFeatures.Add(feature);
                    _logger.LogInformation("Paused Pandora's Box feature: {Feature}", feature);
                }
            }
            catch (IpcError e)
            {
                _logger.LogWarning(e, "Failed to pause Pandora's Box feature: {Feature}", feature);
            }
        }
    }

    private void RestoreConflictingFeatures()
    {
        if (_pausedFeatures == null)
            return;

        foreach (var feature in _pausedFeatures)
        {
            try
            {
                _setFeatureEnabled.InvokeAction(feature, true);
                _logger.LogInformation("Restored Pandora's Box feature: {Feature}", feature);
            }
            catch (IpcError e)
            {
                _logger.LogWarning(e, "Failed to restore Pandora's Box feature: {Feature}", feature);
            }
        }

        _pausedFeatures = null;
    }
}
