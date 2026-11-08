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

    private Session? _session;
    private bool _rebornInstalled;

    public bool HasQuestBattle => _session is { IsQuestBattle: true };
    public bool HasPendingCleanup => _session is { Stopping: true };
    public string? CleanupReason => _session?.Reason;

    // GetActive alone cannot establish idle: newer BossMod also returns null for multiple presets.
    public bool CanConfirmStopped
    {
        get
        {
            try
            {
                return _session is { } session &&
                    (session.ProviderLost || (session.ControlLost && session.Settings.All(x => !x.Pending))) &&
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
        _session = null;
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
    public bool IsRebornInstalled()
    {
        if (EzThrottler.Throttle("IsRebornInstalled", miliseconds: 1000))
            _rebornInstalled = IPCSubscriber.IsInstalled("BossModReborn");
        return _rebornInstalled;
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

    public void SetActivePreset(EPreset preset) => ActivatePreset(preset, questBattle: false);

    public void SetQuestBattlePreset(EPreset preset)
    {
        ActivatePreset(preset, questBattle: true);
    }

    public void StartQuestBattle(uint cfcId)
    {
        // Pâtisserie needs movement and interactions without a combat rotation.
        SetQuestBattlePreset(cfcId == 1045 ? EPreset.NormalMovement : EPreset.QuestBattle);
    }

    private void ActivatePreset(EPreset preset, bool questBattle)
    {
        Session session = Acquire(questBattle);
        try
        {
            if (!session.HasPresetSnapshot)
            {
                if (ActivePresetCount() > 1)
                    throw new TaskException("BossMod has multiple active presets; stop them before starting Questionable.");
                session.PreviousPreset = _getActivePreset();
                session.HasPresetSnapshot = true;
            }
            OverrideSetting(session, "AutorotationConfig", "ClearPresetOnCombatEnd", false);
            if (questBattle)
            {
                OverrideSetting(session, "AutorotationConfig", "ClearPresetOnDeath", false);
                OverrideSetting(session, "ZoneModuleConfig", "EnableQuestBattles", true);
            }
            PresetDefinition definition = AddPreset(preset);
            OverrideSetting(session, "AIConfig", "Enabled", false);

            string? previousExpected = session.ExpectedPreset;
            bool previousPending = session.PresetPending;
            session.ExpectedPreset = definition.Name;
            session.PresetPending = true; // SetActive can apply its change and then throw.
            if (!_setPreset(definition.Name))
            {
                session.ExpectedPreset = previousExpected;
                session.PresetPending = previousPending;
                throw new TaskException($"BossMod rejected activation of preset '{definition.Name}'.");
            }
        }
        catch (Exception e)
        {
            MarkFailed(session, e);
            throw new TaskException(session.Reason!, e);
        }
    }

    private Session Acquire(bool questBattle)
    {
        if (HasPendingCleanup)
            throw new TaskException(CleanupReason!);
        if (_session != null)
        {
            VerifyPreset();
            _session.IsQuestBattle |= questBattle;
            return _session;
        }

        if (!ProviderAvailable())
            throw new TaskException("BossMod is unavailable.");
        if (ActivePresetCount() > 1)
            throw new TaskException("BossMod has multiple active presets; stop them before starting Questionable.");

        return _session = new Session(questBattle);
    }

    public void VerifyPreset()
    {
        if (_session is not { } session)
            throw new TaskException("Questionable's BossMod preset is no longer active.");
        if (session.Stopping)
            throw new TaskException(session.Reason!);
        try
        {
            EnsureProvider(session);
            if (session.PresetPending &&
                ((ActivePresetCount() > 1) ||
                 !string.Equals(_getActivePreset(), session.ExpectedPreset, StringComparison.Ordinal)))
            {
                session.ControlLost = true;
                session.PresetPending = false;
                throw new TaskException("BossMod's active preset changed. Stop its presets, then confirm it is stopped.");
            }
        }
        catch (Exception e)
        {
            MarkFailed(session, e);
            throw new TaskException(session.Reason!, e);
        }
    }

    public void PollQuestBattle()
    {
        if (HasPendingCleanup)
        {
            if (!StopPreset())
                throw new TaskException(CleanupReason!);
        }
        else if (HasQuestBattle)
            VerifyPreset();
    }

    public void StopQuestBattle()
    {
        if (HasQuestBattle && !StopPreset())
            throw new TaskException(CleanupReason!);
    }

    public bool PausePreset() => SetPaused(paused: true);

    public bool ResumePreset() => SetPaused(paused: false);

    private bool SetPaused(bool paused)
    {
        if (_session is not { } session)
            return false;
        try
        {
            VerifyPreset();
            if (paused)
            {
                if (session.PausedPreset != null)
                    return true;
                session.PausedPreset = session.ExpectedPreset ??
                    throw new TaskException("Questionable has no BossMod preset to pause.");
            }
            string? desired = paused ? null : session.PausedPreset ??
                throw new TaskException("Questionable has no paused BossMod preset to resume.");
            session.TransitionFromPreset = session.ExpectedPreset;
            session.HasPresetTransition = true;
            session.ExpectedPreset = desired;
            // Retain both possible states for cleanup if an IPC applies and then throws.
            bool accepted = desired == null ? ClearPreset() : _setPreset(desired);
            if (!accepted ||
                (ActivePresetCount() > 1) ||
                !string.Equals(_getActivePreset(), desired, StringComparison.Ordinal))
                throw new TaskException("BossMod rejected the combat item rotation change.");
            session.HasPresetTransition = false;
            if (!paused)
                session.PausedPreset = null;
            return true;
        }
        catch (Exception e)
        {
            MarkFailed(session, e);
            return false;
        }
    }

    public bool StopPreset()
    {
        if (_session is not { } session)
            return true;
        session.Stopping = true;
        try
        {
            EnsureProvider(session);
            if (session.PresetPending)
            {
                string? current = _getActivePreset();
                // GetActive returns null for both an empty list and multiple active presets.
                bool multiplePresets = ActivePresetCount() > 1;
                if (!multiplePresets &&
                    (string.Equals(current, session.ExpectedPreset, StringComparison.Ordinal) ||
                     (session.HasPresetTransition && string.Equals(current, session.TransitionFromPreset, StringComparison.Ordinal))))
                {
                    bool accepted = session.PreviousPreset == null
                        ? ClearPreset()
                        : _setPreset(session.PreviousPreset);
                    if (!accepted)
                        throw new TaskException("BossMod rejected restoring the previous preset.");
                }
                else if (multiplePresets || !string.Equals(current, session.PreviousPreset, StringComparison.Ordinal))
                {
                    session.ControlLost = true;
                    session.Reason = "BossMod's active preset changed. Stop its presets, then confirm it is stopped.";
                }
                session.PresetPending = false;
            }
        }
        catch (Exception e)
        {
            MarkFailed(session, e);
        }

        // Settings are restored even if restoring the preset failed.
        bool settingsRestored = !session.ProviderLost && RestoreSettings(session);
        if (session.ProviderLost || session.ControlLost || session.PresetPending || !settingsRestored)
            return false;
        _session = null;
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
        if (!available && _session is { } session)
        {
            session.ProviderLost = session.Stopping = true;
            session.Reason = ProviderLostReason;
        }
        return available;
    }

    private void EnsureProvider(Session session)
    {
        if (session.ProviderLost || !ProviderAvailable())
        {
            session.ProviderLost = true;
            throw new TaskException(ProviderLostReason);
        }
    }

    private void MarkFailed(Session session, Exception exception)
    {
        session.Stopping = true;
        if (!ProviderAvailable())
            session.ProviderLost = true;
        session.Reason = session.ProviderLost
            ? ProviderLostReason
            : exception.Message;
    }

    private void OverrideSetting(Session session, string type, string field, bool value)
    {
        if (session.Settings.Any(x => x.Type == type && x.Field == field))
            return;
        EnsureProvider(session);
        bool previous = ReadSetting(type, field);
        var setting = new SettingOverride(type, field, previous, value);
        session.Settings.Add(setting);
        if (previous != value)
        {
            setting.Pending = true; // A throwing IPC can still have applied the value.
            WriteSetting(type, field, value);
        }
    }

    private bool RestoreSettings(Session session)
    {
        foreach (SettingOverride setting in session.Settings.Where(x => x.Pending))
        {
            try
            {
                EnsureProvider(session);
                if (ReadSetting(setting.Type, setting.Field) == setting.Applied)
                    WriteSetting(setting.Type, setting.Field, setting.Previous);
                setting.Pending = false;
            }
            catch (Exception e)
            {
                MarkFailed(session, e);
                if (session.ProviderLost)
                    break;
            }
        }
        return session.Settings.All(x => !x.Pending);
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

    private sealed class Session(bool questBattle)
    {
        // Settings-only movement work does not snapshot a preset until one is acquired.
        public string? PreviousPreset { get; set; }
        public bool HasPresetSnapshot { get; set; }
        public string? ExpectedPreset { get; set; }
        public string? PausedPreset { get; set; }
        public string? TransitionFromPreset { get; set; }
        public bool HasPresetTransition { get; set; }
        public bool PresetPending { get; set; }
        public bool IsQuestBattle { get; set; } = questBattle;
        public bool Stopping { get; set; }
        public bool ProviderLost { get; set; }
        public bool ControlLost { get; set; }
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

    public bool CanRunQuestBattle(ElementId questId, SinglePlayerDutyOptions? dutyOptions)
    {
        if (!IsSupported())
            return false;

        if (!_configuration.SinglePlayerDuties.RunSoloInstancesWithBossMod)
            return false;

        dutyOptions ??= new();
        if (!_territoryData.TryGetQuestBattleCfc(questId, dutyOptions.Index, out TerritoryData.CfcData? cfcData))
            return false;

        if (_configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Contains(cfcData.Id))
            return false;

        if (_configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Contains(cfcData.Id))
            return true;

        return dutyOptions.Enabled;
    }

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);

    public sealed class PresetDefinition(string name, string fileName)
    {
        public string Name { get; } = name;
        public string Content { get; } = Load(fileName);

        public static string Load(string name)
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

    public bool Pause() => bossModIpc.PausePreset();

    public bool Resume() => bossModIpc.ResumePreset();

    public bool Stop()
    {
        try
        {
            return bossModIpc.StopPreset();
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

    public void VerifyActive() => bossModIpc.VerifyPreset();

    public bool CanAttack(IBattleNpc target) => true;

    public void Dispose() => IpcInvoke.TryOnFrameworkThread(framework, () => Stop(), logger);
}
