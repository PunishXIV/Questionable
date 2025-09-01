using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using Questionable.Data;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace Questionable.External;

internal sealed class BossModIpc
{
    private const string PluginName = "BossMod";

    private static readonly ReadOnlyDictionary<EPreset, PresetDefinition> PresetDefinitions = new Dictionary<EPreset, PresetDefinition>
        {
            { EPreset.Overworld, new PresetDefinition("Questionable", "Overworld") },
            { EPreset.QuestBattle, new PresetDefinition("Questionable - Quest Battles", "QuestBattle") },
        }.AsReadOnly();

    private readonly Configuration _configuration;
    private readonly ICommandManager _commandManager;
    private readonly TerritoryData _territoryData;
    private readonly ICallGateSubscriber<string, string?> _getPreset;
    private readonly ICallGateSubscriber<string, bool, bool> _createPreset;
    private readonly ICallGateSubscriber<string, bool> _setPreset;
    private readonly ICallGateSubscriber<bool> _clearPreset;

    public BossModIpc(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        ICommandManager commandManager,
        TerritoryData territoryData)
    {
        _configuration = configuration;
        _commandManager = commandManager;
        _territoryData = territoryData;

        _getPreset = pluginInterface.GetIpcSubscriber<string, string?>($"{PluginName}.Presets.Get");
        _createPreset = pluginInterface.GetIpcSubscriber<string, bool, bool>($"{PluginName}.Presets.Create");
        _setPreset = pluginInterface.GetIpcSubscriber<string, bool>($"{PluginName}.Presets.SetActive");
        _clearPreset = pluginInterface.GetIpcSubscriber<bool>($"{PluginName}.Presets.ClearActive");
    }

    public bool IsSupported()
    {
        try
        {
            return _getPreset.HasFunction;
        }
        catch (IpcError)
        {
            return false;
        }
    }

    public void SetPreset(EPreset preset)
    {
        var definition = PresetDefinitions[preset];
        if (_getPreset.InvokeFunc(definition.Name) == null)
            _createPreset.InvokeFunc(definition.Content, true);

        _setPreset.InvokeFunc(definition.Name);
    }

    public void ClearPreset()
    {
        _clearPreset.InvokeFunc();
    }

    // TODO this should use your actual rotation plugin, not always vbm
    public void EnableAi(bool passive)
    {
        _commandManager.ProcessCommand("/vbmai on");
        _commandManager.ProcessCommand("/vbm cfg ZoneModuleConfig EnableQuestBattles true");
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
        if (!_territoryData.TryGetContentFinderConditionForSoloInstance(questId, dutyOptions.Index, out var cfcData))
            return false;

        if (_configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Contains(cfcData
                .ContentFinderConditionId))
            return false;

        if (_configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Contains(cfcData
                .ContentFinderConditionId))
            return true;

        return dutyOptions.Enabled;
    }

    public enum EPreset
    {
        Overworld,
        QuestBattle,
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
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
