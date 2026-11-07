using System.Collections.Immutable;
using Dalamud.Plugin.Ipc.Exceptions;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class PandorasBoxIpc : Ipc, IDisposable
{
    public override string InternalName => "PandorasBox";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() =>
        {
            return GetVersion() != null && _getFeatureEnabled(ConflictingFeatures.First()) != null;
        }, fallback: false);
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

    [EzIPC("GetFeatureEnabled")] private readonly Func<string, bool?> _getFeatureEnabled = null!;
    private readonly ILogger<PandorasBoxIpc> _logger;
    private readonly QuestController _questController;
    [EzIPC("SetFeatureEnabled")] private readonly Action<string, bool> _setFeatureEnabled = null!;
    private readonly EzIPCDisposalToken[] _disposalTokens;
    private readonly TerritoryData _territoryData;

    private readonly FeatureSuppression _suppression;
    private bool _loggedIpcError;

    public PandorasBoxIpc(IFramework framework,
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
        _suppression = new FeatureSuppression(ConflictingFeatures, logger);
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
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
                return _getFeatureEnabled("Auto Active Time Maneuver") == true;
            }
            catch (IpcNotReadyError)
            {
                return false;
            }
            catch (Exception e) when (IpcInvoke.IsSafeFailure(e))
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
            _setFeatureEnabled("Auto Active Time Maneuver", enabled);
            return IsAutoActiveTimeManeuverEnabled == enabled;
        }
        catch (IpcNotReadyError)
        {
            return false;
        }
        catch (Exception e) when (IpcInvoke.IsSafeFailure(e))
        {
            _logger.LogWarning(e, "Could not set Pandora's Box auto active time maneuver to {Enabled}", enabled);
            return false;
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        IpcInvoke.TryOnFrameworkThread(_framework, () =>
        {
            _suppression.Update(false, IsInstalled, _getFeatureEnabled, _setFeatureEnabled, force: true);
            if (_suppression.TurnedOff.Count > 0)
                _logger.LogWarning("Could not restore Pandora's Box features on unload: {Features}",
                    string.Join(", ", _suppression.TurnedOff));
        }, _logger);
        IPCSubscriber.DisposeAll(_disposalTokens);
    }

    private bool IsInstalled => IPCSubscriber.IsInstalled(InternalName);

    private void OnUpdate(IFramework framework)
    {
        bool hasActiveQuest = (_questController.IsRunning ||
                               _questController.AutomationType != QuestController.EAutomationType.Manual) &&
                              !_territoryData.IsDutyInstance(_clientState.TerritoryType);
        if (!_suppression.HasWork(hasActiveQuest))
            return;

        _suppression.Update(hasActiveQuest, IsInstalled, _getFeatureEnabled, _setFeatureEnabled);
    }

    internal sealed class FeatureSuppression(IEnumerable<string> features, ILogger logger, TimeProvider? timeProvider = null)
    {
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

        private readonly ImmutableArray<string> _features = [..features];
        private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
        private readonly HashSet<string> _turnedOff = [];
        private readonly HashSet<string> _unchecked = [];
        private bool _runActive;
        private bool _warnedThisRun;
        private long? _lastFailureAt;

        public IReadOnlySet<string> TurnedOff => _turnedOff;

        public bool HasWork(bool hasActiveQuest) =>
            hasActiveQuest != _runActive || (hasActiveQuest ? _unchecked.Count > 0 : _turnedOff.Count > 0);

        public void Update(bool hasActiveQuest, bool providerInstalled, Func<string, bool?> isEnabled,
            Action<string, bool> setEnabled, bool force = false)
        {
            if (hasActiveQuest != _runActive)
            {
                _runActive = hasActiveQuest;
                _warnedThisRun = false;
                _lastFailureAt = null;
                _unchecked.Clear();
                if (hasActiveQuest)
                    _unchecked.UnionWith(_features.Where(x => !_turnedOff.Contains(x)));
            }

            HashSet<string> pending = hasActiveQuest ? _unchecked : _turnedOff;
            bool waitingToRetry = !force && _lastFailureAt is { } failedAt &&
                                  _clock.GetElapsedTime(failedAt) < RetryInterval;
            if (pending.Count == 0 || !providerInstalled || waitingToRetry)
                return;

            foreach (string feature in pending.ToList())
            {
                try
                {
                    if (hasActiveQuest)
                    {
                        if (isEnabled(feature) == true)
                        {
                            setEnabled(feature, false);
                            _turnedOff.Add(feature);
                            logger.LogInformation("Paused Pandora's Box feature: {Feature}", feature);
                        }
                    }
                    else
                    {
                        setEnabled(feature, true);
                        logger.LogInformation("Restored Pandora's Box feature: {Feature}", feature);
                    }

                    pending.Remove(feature);
                }
                catch (Exception e) when (IpcInvoke.IsSafeFailure(e))
                {
                    if (!_warnedThisRun)
                        logger.LogWarning(e, "Failed to {Action} Pandora's Box feature {Feature}; retrying",
                            hasActiveQuest ? "pause" : "restore", feature);
                    _warnedThisRun = true;
                }
            }

            _lastFailureAt = pending.Count > 0 ? _clock.GetTimestamp() : null;
        }
    }
}
