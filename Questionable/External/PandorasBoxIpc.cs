using System.Collections.Immutable;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
namespace Questionable.External;

[RegisterSingleton]
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
        "Auto-Sprint in Sanctuaries",
        "Auto-select Turn-ins",
        "Auto-Summon Chocobo",
        "Auto-Sync FATEs",

        // Targets
        "Auto-interact with Gathering Nodes",

        // Other
        "Pandora Quick Gather"
    }.ToImmutableHashSet();
    private readonly IClientState _clientState;

    private readonly IFramework _framework;

    private readonly ICallGateSubscriber<string, bool?> _getFeatureEnabled;
    private readonly ILogger<PandorasBoxIpc> _logger;
    private readonly QuestController _questController;
    private readonly ICallGateSubscriber<string, bool, object?> _setFeatureEnabled;
    private readonly TerritoryData _territoryData;

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
            catch (IpcNotReadyError)
            {
                return false;
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

    public bool SetAutoActiveTimeManeuverEnabled(bool enabled)
    {
        try
        {
            _setFeatureEnabled.InvokeAction("Auto Active Time Maneuver", enabled);
            return IsAutoActiveTimeManeuverEnabled == enabled;
        }
        catch (IpcNotReadyError)
        {
            return false;
        }
        catch (IpcError e)
        {
            _logger.LogWarning(e, "Could not set Pandora's Box auto active time maneuver to {Enabled}", enabled);
            return false;
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        IpcInvoke.TryOnFrameworkThread(_framework, RestoreConflictingFeatures, _logger);
    }

    private void OnUpdate(IFramework framework)
    {
        bool hasActiveQuest = _questController.IsRunning ||
                              _questController.AutomationType != QuestController.EAutomationType.Manual;
        if (hasActiveQuest && !_territoryData.IsDutyInstance(_clientState.TerritoryType))
            DisableConflictingFeatures();
        else
            RestoreConflictingFeatures();
    }

    private void DisableConflictingFeatures()
    {
        if (_pausedFeatures != null)
            return;

        _pausedFeatures = [];

        foreach (string feature in ConflictingFeatures)
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

        foreach (string feature in _pausedFeatures)
        {
            try
            {
                _setFeatureEnabled.InvokeAction(feature, true);
                _logger.LogInformation("Restored Pandora's Box feature: {Feature}", feature);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to restore Pandora's Box feature: {Feature}", feature);
            }
        }

        _pausedFeatures = null;
    }
}
