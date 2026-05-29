using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Questionable.Data;
using Questionable.Model.Questing;
namespace Questionable.External;

internal sealed class BossModIpc
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    TerritoryData territoryData,
    ICommandManager commandManager)
{
    public enum EPreset
    {
        Overworld,
        QuestBattle,
        NormalMovement
    }

    private const string PluginName = "BossMod";

    private static readonly ReadOnlyDictionary<EPreset, PresetDefinition> PresetDefinitions = new Dictionary<EPreset, PresetDefinition>
    {
        { EPreset.Overworld, new("Questionable", "Overworld") },
        { EPreset.QuestBattle, new("Questionable - Quest Battles", "QuestBattle") },
        { EPreset.NormalMovement, new("Questionable - Normal Movement", "NormalMovement") }
    }.AsReadOnly();
    private readonly ICallGateSubscriber<bool> _clearPreset = pluginInterface.GetIpcSubscriber<bool>($"{PluginName}.Presets.ClearActive");

    private readonly ICallGateSubscriber<string, bool, bool> _createPreset = pluginInterface.GetIpcSubscriber<string, bool, bool>($"{PluginName}.Presets.Create");
    private readonly ICallGateSubscriber<string?> _getActivePreset = pluginInterface.GetIpcSubscriber<string?>($"{PluginName}.Presets.GetActive");
    private readonly ICallGateSubscriber<string, string?> _getPreset = pluginInterface.GetIpcSubscriber<string, string?>($"{PluginName}.Presets.Get");
    private readonly ICallGateSubscriber<string, bool> _setPreset = pluginInterface.GetIpcSubscriber<string, bool>($"{PluginName}.Presets.SetActive");

    private bool _soloDutyZoneConfigured;

    public bool IsSupported() => IpcInvoke.SafeFunc(() => _getPreset.HasFunction, false);

    public void SetPreset(EPreset preset)
    {
        PresetDefinition definition = PresetDefinitions[preset];
        if (_getPreset.InvokeFunc(definition.Name) == null)
            _createPreset.InvokeFunc(definition.Content, true);

        commandManager.ProcessCommand("/vbmai off");
        _setPreset.InvokeFunc(definition.Name);
    }

    public void SetPresetForSoloDuty(EPreset preset)
    {
        ConfigureZoneForQuestBattle(true);
        SetPreset(preset);
    }

    public void ClearPreset()
    {
        string? activePreset = _getActivePreset.InvokeFunc();
        if (activePreset == null)
            return;

        foreach (PresetDefinition definition in PresetDefinitions.Values)
        {
            if (definition.Name.Equals(activePreset, StringComparison.Ordinal))
            {
                _clearPreset.InvokeFunc();
                return;
            }
        }
    }

    public void DisableSoloDutyPreset()
    {
        ReleaseSoloDutyZone();
        ClearPreset();
    }

    public void Cleanup()
    {
        commandManager.ProcessCommand("/vbmai off");
        ReleaseSoloDutyZone();
        ClearPreset();
    }

    private void ConfigureZoneForQuestBattle(bool enable)
    {
        commandManager.ProcessCommand(enable
            ? "/vbm cfg ZoneModuleConfig EnableQuestBattles true"
            : "/vbm cfg ZoneModuleConfig EnableQuestBattles false");
        if (enable)
        {
            commandManager.ProcessCommand("/vbm cfg Autorotation ClearPresetOnCombatEnd false");
            _soloDutyZoneConfigured = true;
        }
    }

    private void ReleaseSoloDutyZone()
    {
        if (!_soloDutyZoneConfigured)
            return;

        commandManager.ProcessCommand("/vbm cfg ZoneModuleConfig EnableQuestBattles false");
        _soloDutyZoneConfigured = false;
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

    private sealed class PresetDefinition(string name, string fileName)
    {
        public string Name { get; } = name;
        public string Content { get; } = LoadPreset(fileName);

        private static string LoadPreset(string name)
        {
            Stream stream =
                typeof(BossModIpc).Assembly.GetManifestResourceStream(
                    $"Questionable.Controller.CombatModules.BossModPreset.{name}") ??
                throw new InvalidOperationException($"Preset {name} was not found");
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
    }
}
