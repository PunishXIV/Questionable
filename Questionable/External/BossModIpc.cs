using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using Questionable.Data;
using Questionable.Model.Questing;
namespace Questionable.External;

internal sealed class BossModIpc
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    ICommandManager commandManager,
    TerritoryData territoryData)
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
    private readonly ICommandManager _commandManager = commandManager;

    private readonly Configuration _configuration = configuration;
    private readonly ICallGateSubscriber<string, bool, bool> _createPreset = pluginInterface.GetIpcSubscriber<string, bool, bool>($"{PluginName}.Presets.Create");
    private readonly ICallGateSubscriber<string, string?> _getPreset = pluginInterface.GetIpcSubscriber<string, string?>($"{PluginName}.Presets.Get");
    private readonly ICallGateSubscriber<string, bool> _setPreset = pluginInterface.GetIpcSubscriber<string, bool>($"{PluginName}.Presets.SetActive");
    private readonly TerritoryData _territoryData = territoryData;

    public bool IsSupported() => IpcInvoke.SafeFunc(() => _getPreset.HasFunction, false);

    public void SetPreset(EPreset preset)
    {
        PresetDefinition definition = PresetDefinitions[preset];
        if (_getPreset.InvokeFunc(definition.Name) == null)
            _createPreset.InvokeFunc(definition.Content, true);

        _setPreset.InvokeFunc(definition.Name);
    }

    public void ClearPreset() => _clearPreset.InvokeFunc();

    // TODO this should use your actual rotation plugin, not always vbm
    public void EnableAi(bool passive)
    {
        //_commandManager.ProcessCommand("/vbmai on");
        _commandManager.ProcessCommand("/vbm cfg ZoneModuleConfig EnableQuestBattles true");
        _commandManager.ProcessCommand("/vbm cfg Autorotation ClearPresetOnCombatEnd false");
        Svc.Log.Debug($"Enabling preset {(passive ? "Overworld" : "QuestBattle")}");
        SetPreset(passive ? EPreset.Overworld : EPreset.QuestBattle);
    }

    public void DisableAi()
    {
        _commandManager.ProcessCommand("/vbmai off");
        _commandManager.ProcessCommand("/vbm cfg ZoneModuleConfig EnableQuestBattles false");
        ClearPreset();
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
