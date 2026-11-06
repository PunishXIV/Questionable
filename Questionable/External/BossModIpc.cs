using System.Collections.ObjectModel;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc.Exceptions;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class BossModIpc : Ipc, IDisposable
{
    public override string InternalName => PluginName;
    public override Version? GetVersion() => IPCSubscriber.Version(PluginName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() =>
        {
            var _ = _getActivePreset();
            return GetVersion() != null;
        }, fallback: false);
    public enum EPreset
    {
        Overworld,
        QuestBattle,
        NormalMovement
    }

    private const string PluginName = "BossMod";
    private const string ProviderLostReason = "BossMod disappeared. Check its settings and stop its presets, then confirm it is stopped.";

    private static readonly ReadOnlyDictionary<EPreset, PresetDefinition> PresetDefinitions = new Dictionary<EPreset, PresetDefinition>
    {
        { EPreset.Overworld, new("Questionable", "Overworld") },
        { EPreset.QuestBattle, new("Questionable - Quest Battles", "QuestBattle") },
        { EPreset.NormalMovement, new("Questionable - Normal Movement", "NormalMovement") }
    }.AsReadOnly();

    private readonly Configuration _configuration;
    private readonly TerritoryData _territoryData;
    private readonly ICommandManager _commandManager;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("Presets.ClearActive")] private readonly Func<bool> _clearPreset = null!;
    [EzIPC("Presets.Create")] private readonly Func<string, bool, bool> _createPreset = null!;
    [EzIPC("Presets.GetActive")] private readonly Func<string?> _getActivePreset = null!;
    [EzIPC("Presets.GetActiveList")] private readonly Func<List<string>> _getActivePresets = null!;
    [EzIPC("Presets.Get")] private readonly Func<string, string?> _getPreset = null!;
    [EzIPC("Presets.SetActive")] private readonly Func<string, bool> _setPreset = null!;
    [EzIPC("Presets.Delete")] private readonly Func<string, bool> _deletePreset = null!;
    [EzIPC("Configuration")] private readonly Func<IReadOnlyList<string>, bool, IReadOnlyList<string>> _configurationCommand = null!;

    public BossModIpc(Configuration configuration, TerritoryData territoryData, ICommandManager commandManager)
    {
        _configuration = configuration;
        _territoryData = territoryData;
        _commandManager = commandManager;
        _disposalTokens = EzIPC.Init(this, PluginName, SafeWrapper.None);
    }

    private OwnedPreset? _ownedPreset;
    private bool _bossmodRebornDetected;

    public bool HasOwnedSoloDuty => _ownedPreset is { SoloDuty: true };
    public bool HasPendingCleanup => _ownedPreset is { Stopping: true };
    public string? CleanupReason => _ownedPreset?.Reason;

    // GetActive alone cannot establish idle: newer BossMod also returns null for multiple presets.
    public bool CanConfirmStopped
    {
        get
        {
            try
            {
                return _ownedPreset is { } owned &&
                    (owned.ProviderLost || (owned.OwnershipLost && owned.Settings.All(x => !x.Pending))) &&
                    ProviderAvailable() && ActivePresetCount() == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public bool TryConfirmStopped()
    {
        if (!CanConfirmStopped)
            return false;

        // The old BossMod is gone; don't restore its snapshot into the new one.
        _ownedPreset = null;
        return true;
    }

    public bool IsSupported()
    {
        try
        {
            _getActivePreset();
            return true;
        }
        catch (Exception e)
        {
            return !IpcInvoke.IsMissing(e);
        }
    }

    private int? ActivePresetCount()
    {
        try
        {
            return _getActivePresets().Count;
        }
        catch (Exception e) when (IpcInvoke.IsMissing(e))
        {
            return null;
        }
    }
    public bool BossModRebornDetected()
    {
        if (EzThrottler.Throttle("BossModRebornDetected", miliseconds: 1000))
            _bossmodRebornDetected = IPCSubscriber.IsInstalled("BossModReborn");
        return _bossmodRebornDetected;
    }

    public PresetDefinition AddPreset(EPreset preset) => AddPreset(PresetDefinitions[preset]);
    public PresetDefinition AddPreset(PresetDefinition definition, bool bulk = false)
    {
        if (!bulk)
            ClearObstacleMaps();
        if (_getPreset(definition.Name) == null && !_createPreset(definition.Content, true))
            throw new TaskException($"BossMod rejected creation of preset '{definition.Name}'.");
        return definition;
    }

    public void AddAllPresets(bool delete = false)
    {
        ClearObstacleMaps();
        foreach (PresetDefinition definition in PresetDefinitions.Values)
        {
            if (delete)
                _deletePreset(definition.Name);
            AddPreset(definition, bulk: true);
        }
    }

    public void SetActivePreset(EPreset preset) => ActivatePreset(preset, soloDuty: false);

    public void SetPresetForSoloDuty(EPreset preset)
    {
        ActivatePreset(preset, soloDuty: true);
    }

    public void StartSoloDuty(uint contentFinderConditionId)
    {
        // Pâtisserie needs movement and interactions without a combat rotation.
        SetPresetForSoloDuty(contentFinderConditionId == 1045 ? EPreset.NormalMovement : EPreset.QuestBattle);
    }

    private void ActivatePreset(EPreset preset, bool soloDuty)
    {
        OwnedPreset owned = Acquire(soloDuty);
        try
        {
            if (!owned.HasPresetSnapshot)
            {
                if (ActivePresetCount() > 1)
                    throw new TaskException("BossMod has multiple active presets; stop them before starting Questionable.");
                owned.PreviousPreset = _getActivePreset();
                owned.HasPresetSnapshot = true;
            }
            OverrideSetting(owned, "AutorotationConfig", "ClearPresetOnCombatEnd", false);
            if (soloDuty)
            {
                OverrideSetting(owned, "AutorotationConfig", "ClearPresetOnDeath", false);
                OverrideSetting(owned, "ZoneModuleConfig", "EnableQuestBattles", true);
            }
            PresetDefinition definition = AddPreset(preset);
            OverrideSetting(owned, "AIConfig", "Enabled", false);

            string? previousExpected = owned.ExpectedPreset;
            bool previousPending = owned.PresetPending;
            owned.ExpectedPreset = definition.Name;
            owned.PresetPending = true; // SetActive can apply its change and then throw.
            if (!_setPreset(definition.Name))
            {
                owned.ExpectedPreset = previousExpected;
                owned.PresetPending = previousPending;
                throw new TaskException($"BossMod rejected activation of preset '{definition.Name}'.");
            }
        }
        catch (Exception e)
        {
            FailAcquisition(owned, e);
            throw new TaskException(owned.Reason!, e);
        }
    }

    private OwnedPreset Acquire(bool soloDuty)
    {
        if (HasPendingCleanup)
            throw new TaskException(CleanupReason!);
        if (_ownedPreset != null)
        {
            VerifyOwnedPreset();
            _ownedPreset.SoloDuty |= soloDuty;
            return _ownedPreset;
        }

        if (!ProviderAvailable())
            throw new TaskException("BossMod is unavailable.");
        if (ActivePresetCount() > 1)
            throw new TaskException("BossMod has multiple active presets; stop them before starting Questionable.");

        return _ownedPreset = new OwnedPreset(soloDuty);
    }

    public void VerifyOwnedPreset()
    {
        if (_ownedPreset is not { } owned)
            throw new TaskException("Questionable no longer owns a BossMod preset.");
        if (owned.Stopping)
            throw new TaskException(owned.Reason!);
        try
        {
            EnsureProvider(owned);
            if (owned.PresetPending &&
                ((ActivePresetCount() > 1) ||
                 !string.Equals(_getActivePreset(), owned.ExpectedPreset, StringComparison.Ordinal)))
            {
                owned.OwnershipLost = true;
                owned.PresetPending = false;
                throw new TaskException("BossMod's active preset changed. Stop its presets, then confirm it is stopped.");
            }
        }
        catch (Exception e)
        {
            FailAcquisition(owned, e);
            throw new TaskException(owned.Reason!, e);
        }
    }

    public void PollSoloDuty()
    {
        if (HasPendingCleanup)
        {
            if (!StopOwnedPreset())
                throw new TaskException(CleanupReason!);
        }
        else if (HasOwnedSoloDuty)
            VerifyOwnedPreset();
    }

    public void DisableSoloDutyPreset()
    {
        if (HasOwnedSoloDuty && !StopOwnedPreset())
            throw new TaskException(CleanupReason!);
    }

    public void Cleanup()
    {
        if (!StopOwnedPreset())
            throw new TaskException(CleanupReason!);
    }

    public bool PauseOwnedPreset() => ChangeOwnedPreset(paused: true);

    public bool ResumeOwnedPreset() => ChangeOwnedPreset(paused: false);

    private bool ChangeOwnedPreset(bool paused)
    {
        if (_ownedPreset is not { } owned)
            return false;
        try
        {
            VerifyOwnedPreset();
            if (paused)
            {
                if (owned.PausedPreset != null)
                    return true;
                owned.PausedPreset = owned.ExpectedPreset ??
                    throw new TaskException("Questionable has no BossMod preset to pause.");
            }
            string? desired = paused ? null : owned.PausedPreset ??
                throw new TaskException("Questionable has no paused BossMod preset to resume.");
            owned.TransitionFromPreset = owned.ExpectedPreset;
            owned.HasPresetTransition = true;
            owned.ExpectedPreset = desired;
            // Retain both possible states for cleanup if an IPC applies and then throws.
            bool accepted = desired == null ? ClearPreset() : _setPreset(desired);
            if (!accepted ||
                (ActivePresetCount() > 1) ||
                !string.Equals(_getActivePreset(), desired, StringComparison.Ordinal))
                throw new TaskException("BossMod rejected the combat item rotation change.");
            owned.HasPresetTransition = false;
            if (!paused)
                owned.PausedPreset = null;
            return true;
        }
        catch (Exception e)
        {
            FailAcquisition(owned, e);
            return false;
        }
    }

    public bool StopOwnedPreset()
    {
        if (_ownedPreset is not { } owned)
            return true;
        owned.Stopping = true;
        try
        {
            EnsureProvider(owned);
            if (owned.PresetPending)
            {
                string? current = _getActivePreset();
                // GetActive returns null for both an empty list and multiple active presets.
                bool multiplePresets = ActivePresetCount() > 1;
                if (!multiplePresets &&
                    (string.Equals(current, owned.ExpectedPreset, StringComparison.Ordinal) ||
                     (owned.HasPresetTransition && string.Equals(current, owned.TransitionFromPreset, StringComparison.Ordinal))))
                {
                    bool accepted = owned.PreviousPreset == null
                        ? ClearPreset()
                        : _setPreset(owned.PreviousPreset);
                    if (!accepted)
                        throw new TaskException("BossMod rejected restoring the previous preset.");
                }
                else if (multiplePresets || !string.Equals(current, owned.PreviousPreset, StringComparison.Ordinal))
                {
                    owned.OwnershipLost = true;
                    owned.Reason = "BossMod's active preset changed. Stop its presets, then confirm it is stopped.";
                }
                owned.PresetPending = false;
            }
        }
        catch (Exception e)
        {
            FailAcquisition(owned, e);
        }

        // Settings are restored even if restoring the preset failed.
        bool settingsRestored = !owned.ProviderLost && RestoreSettings(owned);
        if (owned.ProviderLost || owned.OwnershipLost || owned.PresetPending || !settingsRestored)
            return false;
        _ownedPreset = null;
        return true;
    }

    // ClearActive returns false when no preset is active, which is already the state we want.
    private bool ClearPreset() =>
        _clearPreset() ||
        (_getActivePreset() == null && ActivePresetCount() is null or 0);

    public void ClearObstacleMaps() => _commandManager.ProcessCommand("/vbm clear-maps");

    private bool ProviderAvailable()
    {
        bool available;
        try
        {
            available = GetVersion() != null && IsSupported();
        }
        catch (Exception)
        {
            available = false;
        }
        if (!available && _ownedPreset is { } owned)
        {
            owned.ProviderLost = owned.Stopping = true;
            owned.Reason = ProviderLostReason;
        }
        return available;
    }

    private void EnsureProvider(OwnedPreset owned)
    {
        if (owned.ProviderLost || !ProviderAvailable())
        {
            owned.ProviderLost = true;
            throw new TaskException(ProviderLostReason);
        }
    }

    private void FailAcquisition(OwnedPreset owned, Exception exception)
    {
        owned.Stopping = true;
        if (!ProviderAvailable())
            owned.ProviderLost = true;
        owned.Reason = owned.ProviderLost
            ? ProviderLostReason
            : exception.Message;
    }

    private void OverrideSetting(OwnedPreset owned, string type, string field, bool value)
    {
        if (owned.Settings.Any(x => x.Type == type && x.Field == field))
            return;
        EnsureProvider(owned);
        bool previous = ReadSetting(type, field);
        var setting = new SettingOverride(type, field, previous, value);
        owned.Settings.Add(setting);
        if (previous != value)
        {
            setting.Pending = true; // A throwing IPC can still have applied the value.
            WriteSetting(type, field, value);
        }
    }

    private bool RestoreSettings(OwnedPreset owned)
    {
        foreach (SettingOverride setting in owned.Settings.Where(x => x.Pending))
        {
            try
            {
                EnsureProvider(owned);
                if (ReadSetting(setting.Type, setting.Field) == setting.Applied)
                    WriteSetting(setting.Type, setting.Field, setting.Previous);
                setting.Pending = false;
            }
            catch (Exception e)
            {
                FailAcquisition(owned, e);
                if (owned.ProviderLost)
                    break;
            }
        }
        return owned.Settings.All(x => !x.Pending);
    }

    private bool ReadSetting(string type, string field)
    {
        // BossMod's Configuration endpoint calls ConfigRoot.ConsoleCommand directly, without "cfg".
        IReadOnlyList<string> result = _configurationCommand([type, field], false);
        if (result.Count != 1 || !bool.TryParse(result[0], out bool value))
            throw new TaskException($"Unable to read BossMod setting {type}.{field}.");
        return value;
    }

    private void WriteSetting(string type, string field, bool value)
    {
        IReadOnlyList<string> result = _configurationCommand([type, field, value ? "true" : "false"], false);
        if (result.Count != 0 || ReadSetting(type, field) != value)
            throw new TaskException($"BossMod rejected setting {type}.{field} to {value}.");
    }

    private sealed class OwnedPreset(bool soloDuty)
    {
        // Settings-only movement work does not snapshot a preset until one is acquired.
        public string? PreviousPreset { get; set; }
        public bool HasPresetSnapshot { get; set; }
        public string? ExpectedPreset { get; set; }
        public string? PausedPreset { get; set; }
        public string? TransitionFromPreset { get; set; }
        public bool HasPresetTransition { get; set; }
        public bool PresetPending { get; set; }
        public bool SoloDuty { get; set; } = soloDuty;
        public bool Stopping { get; set; }
        public bool ProviderLost { get; set; }
        public bool OwnershipLost { get; set; }
        public string? Reason { get; set; }
        public List<SettingOverride> Settings { get; } = [];
    }

    private sealed class SettingOverride(string type, string field, bool previous, bool applied)
    {
        public string Type { get; } = type;
        public string Field { get; } = field;
        public bool Previous { get; } = previous;
        public bool Applied { get; } = applied;
        public bool Pending { get; set; }
    }

    public bool IsConfiguredToRunSoloInstance(ElementId questId, SinglePlayerDutyOptions? dutyOptions)
    {
        if (!IsSupported())
            return false;

        if (!_configuration.SinglePlayerDuties.RunSoloInstancesWithBossMod)
            return false;

        dutyOptions ??= new();
        if (!_territoryData.TryGetContentFinderConditionForSoloInstance(questId, dutyOptions.Index, out TerritoryData.ContentFinderConditionData? cfcData))
            return false;

        if (_configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Contains(cfcData.ContentFinderConditionId))
            return false;

        if (_configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Contains(cfcData.ContentFinderConditionId))
            return true;

        return dutyOptions.Enabled;
    }

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);

    public sealed class PresetDefinition(string name, string fileName)
    {
        public string Name { get; } = name;
        public string Content { get; } = LoadPreset(fileName);

        public static string LoadPreset(string name)
        {
            Stream stream =
                typeof(BossModIpc).Assembly.GetManifestResourceStream(
                    $"Questionable.Controller.CombatModules.BossModPreset.{name}") ??
                throw new InvalidOperationException(_LF("Preset {0} was not found", name));
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
    }
}

[RegisterSingleton<ICombatModule, BossModModule>(Duplicate = DuplicateStrategy.Append)]
internal sealed class BossModModule
(
    ILogger<BossModModule> logger,
    BossModIpc bossModIpc,
    Configuration configuration,
    IFramework framework) : ICombatModule, IDisposable
{
    public string Name => "Boss Mod";

    private bool _justLoaded = true;

    public bool CanHandleFight(CombatController.CombatData combatData)
    {
        if (configuration.General.CombatModule != ECombatModule.BossMod)
            return false;

        return bossModIpc.IsSupported();
    }

    public bool Start(CombatController.CombatData combatData)
    {
        try
        {
            if (_justLoaded)
            {
                bossModIpc.AddAllPresets(delete: true);
                _justLoaded = false;
            }
            bossModIpc.SetActivePreset(BossModIpc.EPreset.Overworld);
            return true;
        }
        catch (IpcError e)
        {
            logger.LogWarning(e, "Could not start combat");
            return false;
        }
    }

    public bool Pause() => bossModIpc.PauseOwnedPreset();

    public bool Resume() => bossModIpc.ResumeOwnedPreset();

    public bool Stop()
    {
        try
        {
            return bossModIpc.StopOwnedPreset();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not turn off combat");
            return false;
        }
    }

    public void Update(IGameObject gameObject)
    {
    }

    public void VerifyActive() => bossModIpc.VerifyOwnedPreset();

    public bool CanAttack(IBattleNpc target) => true;

    public void Dispose() => IpcInvoke.TryOnFrameworkThread(framework, () => Stop(), logger);
}
