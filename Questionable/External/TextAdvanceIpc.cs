using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Questionable.Controller;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Questionable.External;

internal sealed class TextAdvanceIpc : IDisposable
{
    private bool _isExternalControlActivated;
    private readonly QuestController _questController;
    private readonly Configuration _configuration;
    private readonly IFramework _framework;
    private readonly ICallGateSubscriber<bool> _isInExternalControl;
    private readonly ICallGateSubscriber<string, ExternalTerritoryConfig, bool> _enableExternalControl;
    private readonly ICallGateSubscriber<string, bool> _disableExternalControl;
    private readonly string _pluginName;
    private readonly ExternalTerritoryConfig _externalTerritoryConfig = new();

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
        {
            _disableExternalControl.InvokeFunc(_pluginName);
        }
    }

    private void OnUpdate(IFramework framework)
    {
        bool hasActiveQuest = _questController.IsRunning ||
                              _questController.AutomationType != QuestController.EAutomationType.Manual;
        if (_configuration.General.ConfigureTextAdvance && hasActiveQuest)
        {
            if (!_isInExternalControl.InvokeFunc())
            {
                if (_enableExternalControl.InvokeFunc(_pluginName, _externalTerritoryConfig))
                {
                    _isExternalControlActivated = true;
                }
            }
        }
        else
        {
            if (_isExternalControlActivated)
            {
                if (_disableExternalControl.InvokeFunc(_pluginName) || !_isInExternalControl.InvokeFunc())
                {
                    _isExternalControlActivated = false;
                }
            }
        }
    }

    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public sealed class ExternalTerritoryConfig
    {
#pragma warning disable CS0414 // Field is assigned but its value is never used
        public bool? EnableQuestAccept = true;
        public bool? EnableQuestComplete = true;
        public bool? EnableRewardPick = true;
        public bool? EnableRequestHandin = true;
        public bool? EnableCutsceneEsc = true;
        public bool? EnableCutsceneSkipConfirm = true;
        public bool? EnableTalkSkip = true;
        public bool? EnableRequestFill = true;
        public bool? EnableAutoInteract = false;
#pragma warning restore CS0414 // Field is assigned but its value is never used
    }
}
