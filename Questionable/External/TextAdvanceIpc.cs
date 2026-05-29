using System;
using System.Diagnostics.CodeAnalysis;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Questionable.Controller;
namespace Questionable.External;

internal sealed class TextAdvanceIpc : IDisposable
{
    private readonly Configuration _configuration;
    private readonly ICallGateSubscriber<string, bool> _disableExternalControl;
    private readonly ICallGateSubscriber<string, ExternalTerritoryConfig, bool> _enableExternalControl;
    private readonly IFramework _framework;
    private readonly ICallGateSubscriber<bool> _isInExternalControl;
    private readonly string _pluginName;
    private readonly QuestController _questController;
    private bool _isExternalControlActivated;

    public TextAdvanceIpc(IDalamudPluginInterface pluginInterface, IFramework framework,
        QuestController questController, Configuration configuration)
    {
        _framework = framework;
        _questController = questController;
        _configuration = configuration;
        _isInExternalControl = pluginInterface.GetIpcSubscriber<bool>("TextAdvance.IsInExternalControl");
        _enableExternalControl =
            pluginInterface.GetIpcSubscriber<string, ExternalTerritoryConfig, bool>(
                "TextAdvance.EnableExternalControl");
        _disableExternalControl = pluginInterface.GetIpcSubscriber<string, bool>("TextAdvance.DisableExternalControl");
        _pluginName = pluginInterface.InternalName;
        _framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        if (_isExternalControlActivated)
            _disableExternalControl.InvokeFunc(_pluginName);
    }

    private void OnUpdate(IFramework framework)
    {
        bool hasActiveQuest = _questController.IsRunning ||
                              _questController.AutomationType != QuestController.EAutomationType.Manual;
        if (_configuration.General.ConfigureTextAdvance && hasActiveQuest)
        {
            if (!_isInExternalControl.InvokeFunc())
            {
                if (_enableExternalControl.InvokeFunc(
                    _pluginName, CreateExternalTerritoryConfig(_configuration.General.DontSkipCutscenes)))
                    _isExternalControlActivated = true;
            }
        }
        else
        {
            if (_isExternalControlActivated)
            {
                if (_disableExternalControl.InvokeFunc(_pluginName) || !_isInExternalControl.InvokeFunc())
                    _isExternalControlActivated = false;
            }
        }
    }

    private static ExternalTerritoryConfig CreateExternalTerritoryConfig(bool dontSkipCutscenes) => new()
    {
        EnableQuestAccept = true,
        EnableQuestComplete = true,
        EnableRewardPick = true,
        EnableRequestHandin = true,
        EnableCutsceneEsc = !dontSkipCutscenes,
        EnableCutsceneSkipConfirm = !dontSkipCutscenes,
        EnableTalkSkip = !dontSkipCutscenes,
        EnableRequestFill = true,
        EnableAutoInteract = false
    };

    private sealed class ExternalTerritoryConfig
    {
        public bool? EnableQuestAccept;
        public bool? EnableQuestComplete;
        public bool? EnableRewardPick;
        public bool? EnableRequestHandin;
        public bool? EnableCutsceneEsc;
        public bool? EnableCutsceneSkipConfirm;
        public bool? EnableTalkSkip;
        public bool? EnableRequestFill;
        public bool? EnableAutoInteract;
    }
}
