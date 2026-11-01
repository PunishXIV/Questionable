using System.Collections.ObjectModel;
using Dalamud.Plugin.Ipc;
using Questionable.Model.Questing;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class BossModIpc
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    TerritoryData territoryData,
    ICommandManager commandManager) : Ipc
{
    public override string InternalName => PluginName;
    public override Version? GetVersion() => pluginInterface.InstalledPlugins
        .FirstOrDefault(p => p.InternalName == PluginName && p.IsLoaded)?.Version;
    public override bool IsReady() => IpcInvoke.SafeFunc(() =>
        {
            var _ = _getActivePreset.InvokeFunc();
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
    private readonly ICallGateSubscriber<bool> _clearPreset = pluginInterface.GetIpcSubscriber<bool>($"{PluginName}.Presets.ClearActive");

    private readonly ICallGateSubscriber<string, bool, bool> _createPreset = pluginInterface.GetIpcSubscriber<string, bool, bool>($"{PluginName}.Presets.Create");
    private readonly ICallGateSubscriber<string?> _getActivePreset = pluginInterface.GetIpcSubscriber<string?>($"{PluginName}.Presets.GetActive");
    private readonly ICallGateSubscriber<List<string>> _getActivePresets = pluginInterface.GetIpcSubscriber<List<string>>($"{PluginName}.Presets.GetActiveList");
    private readonly ICallGateSubscriber<string, string?> _getPreset = pluginInterface.GetIpcSubscriber<string, string?>($"{PluginName}.Presets.Get");
    private readonly ICallGateSubscriber<string, bool> _setPreset = pluginInterface.GetIpcSubscriber<string, bool>($"{PluginName}.Presets.SetActive");
    private readonly ICallGateSubscriber<string, bool> _deletePreset = pluginInterface.GetIpcSubscriber<string, bool>($"{PluginName}.Presets.Delete");
    private readonly ICallGateSubscriber<IReadOnlyList<string>, bool, IReadOnlyList<string>> _configuration =
        pluginInterface.GetIpcSubscriber<IReadOnlyList<string>, bool, IReadOnlyList<string>>($"{PluginName}.Configuration");

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
                    ProviderAvailable() && _getActivePresets.HasFunction && _getActivePresets.InvokeFunc().Count == 0;
            }
            catch (Exception)
            {
                // A failed provider query is unknown, never a safe state for the confirmation UI.
                return false;
            }
        }
    }

    public bool TryConfirmStopped()
    {
        if (!CanConfirmStopped)
            return false;

        // Never replay snapshots against a provider that disappeared, even after acknowledgement.
        _ownedPreset = null;
        return true;
    }

    public bool IsSupported() => IpcInvoke.SafeFunc(() => _getPreset.HasFunction, fallback: false);
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
        if (_getPreset.InvokeFunc(definition.Name) == null && !_createPreset.InvokeFunc(definition.Content, true))
            throw new TaskException($"BossMod rejected creation of preset '{definition.Name}'.");
        return definition;
    }

    public void AddAllPresets(bool delete = false)
    {
        ClearObstacleMaps();
        foreach (PresetDefinition definition in PresetDefinitions.Values)
        {
            if (delete)
                _deletePreset.InvokeFunc(definition.Name);
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

    public void UnsetBasicSettings()
    {
        if (!configuration.Advanced.BMRestoreMovement)
            return;

        OwnedPreset owned = Acquire(soloDuty: false);
        try
        {
            OverrideSetting(owned, "ActionTweaksConfig", "PreventMovingWhileCasting", false);
        }
        catch (Exception e)
        {
            FailAcquisition(owned, e);
            throw new TaskException(owned.Reason!, e);
        }
    }

    private void ActivatePreset(EPreset preset, bool soloDuty)
    {
        OwnedPreset owned = Acquire(soloDuty);
        try
        {
            if (!owned.HasPresetSnapshot)
            {
                if (_getActivePresets.HasFunction && _getActivePresets.InvokeFunc().Count > 1)
                    throw new TaskException("BossMod has multiple active presets; stop them before starting Questionable.");
                owned.PreviousPreset = _getActivePreset.InvokeFunc();
                owned.HasPresetSnapshot = true;
            }
            OverrideSetting(owned, "AutorotationConfig", "ClearPresetOnCombatEnd", false);
            if (soloDuty)
            {
                if (configuration.Advanced.BMRestoreMovement)
                    OverrideSetting(owned, "ActionTweaksConfig", "PreventMovingWhileCasting", false);
                OverrideSetting(owned, "AutorotationConfig", "ClearPresetOnDeath", false);
                OverrideSetting(owned, "ZoneModuleConfig", "EnableQuestBattles", true);
            }
            PresetDefinition definition = AddPreset(preset);
            OverrideSetting(owned, "AIConfig", "Enabled", false);

            string? previousExpected = owned.ExpectedPreset;
            bool previousPending = owned.PresetPending;
            owned.ExpectedPreset = definition.Name;
            owned.PresetPending = true; // SetActive can apply its change and then throw.
            if (!_setPreset.InvokeFunc(definition.Name))
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
        if (_getActivePresets.HasFunction && _getActivePresets.InvokeFunc().Count > 1)
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
            if (owned.PresetPending && !string.Equals(_getActivePreset.InvokeFunc(), owned.ExpectedPreset, StringComparison.Ordinal))
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

    /// <summary>Poll before controller early returns. Healthy solo ownership permits local duty tasks.</summary>
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
                string? current = _getActivePreset.InvokeFunc();
                // GetActive returns null for both an empty list and multiple active presets.
                bool multiplePresets = _getActivePresets.HasFunction && _getActivePresets.InvokeFunc().Count > 1;
                if (!multiplePresets && string.Equals(current, owned.ExpectedPreset, StringComparison.Ordinal))
                {
                    bool accepted = owned.PreviousPreset == null
                        ? _clearPreset.InvokeFunc()
                        : _setPreset.InvokeFunc(owned.PreviousPreset);
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

        // Preset failure does not discard independent setting-restoration obligations.
        bool settingsRestored = !owned.ProviderLost && RestoreSettings(owned);
        if (owned.ProviderLost || owned.OwnershipLost || owned.PresetPending || !settingsRestored)
            return false;
        _ownedPreset = null;
        return true;
    }

    public void ClearObstacleMaps() => commandManager.ProcessCommand("/vbm clear-maps");

    private bool ProviderAvailable()
    {
        bool available;
        try
        {
            available = _getActivePreset.HasFunction && _setPreset.HasFunction &&
                _clearPreset.HasFunction && GetVersion() != null;
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
        IReadOnlyList<string> result = _configuration.InvokeFunc([type, field], false);
        if (result.Count != 1 || !bool.TryParse(result[0], out bool value))
            throw new TaskException($"Unable to read BossMod setting {type}.{field}.");
        return value;
    }

    private void WriteSetting(string type, string field, bool value)
    {
        IReadOnlyList<string> result = _configuration.InvokeFunc([type, field, value ? "true" : "false"], false);
        if (result.Count != 0 || ReadSetting(type, field) != value)
            throw new TaskException($"BossMod rejected setting {type}.{field} to {value}.");
    }

    private sealed class OwnedPreset(bool soloDuty)
    {
        // Settings-only movement work does not snapshot a preset until one is acquired.
        public string? PreviousPreset { get; set; }
        public bool HasPresetSnapshot { get; set; }
        public string? ExpectedPreset { get; set; }
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

        if (!configuration.SinglePlayerDuties.RunSoloInstancesWithBossMod)
            return false;

        dutyOptions ??= new();
        if (!territoryData.TryGetContentFinderConditionForSoloInstance(questId, dutyOptions.Index, out TerritoryData.ContentFinderConditionData? cfcData))
            return false;

        if (configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Contains(cfcData.ContentFinderConditionId))
            return false;

        if (configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Contains(cfcData.ContentFinderConditionId))
            return true;

        return dutyOptions.Enabled;
    }

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
