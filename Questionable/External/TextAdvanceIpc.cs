using Dalamud.Plugin.Ipc;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class TextAdvanceIpc : Ipc, IDisposable
{
    public override string InternalName => "TextAdvance";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() =>
        {
            var _ = _isInExternalControl.InvokeFunc();
            return GetVersion() != null;
        }, fallback: false);
    private readonly Configuration _configuration;
    private readonly ICallGateSubscriber<string, bool> _disableExternalControl;
    private readonly ICallGateSubscriber<string, ExternalTerritoryConfig, bool> _enableExternalControl;
    private readonly IFramework _framework;
    private readonly ICallGateSubscriber<bool> _isInExternalControl;
    private readonly string _pluginName;
    private readonly QuestController _questController;
    private bool _isExternalControlActivated;
    private bool _sentDontSkipCutscenes;

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
            IpcInvoke.TryOnFrameworkThread(_framework, () => _disableExternalControl.InvokeFunc(_pluginName));
    }

    private void OnUpdate(IFramework framework)
    {
        bool hasActiveQuest = _questController.IsRunning ||
                              _questController.AutomationType != QuestController.EAutomationType.Manual;
        bool dontSkipCutscenes = _configuration.General.DontSkipCutscenes;
        // A missing or unloading TextAdvance must not throw from every framework update.
        if (_configuration.General.ConfigureTextAdvance && hasActiveQuest)
        {
            if (_isExternalControlActivated && dontSkipCutscenes != _sentDontSkipCutscenes)
            {
                // Settings are only sent with control; release it so the next update re-acquires with the new ones.
                if (IpcInvoke.SafeFunc(() => _disableExternalControl.InvokeFunc(_pluginName), fallback: false))
                    _isExternalControlActivated = false;
            }
            else if (IpcInvoke.SafeFunc<bool?>(() => _isInExternalControl.InvokeFunc(), fallback: null) == false &&
                     IpcInvoke.SafeFunc(() => _enableExternalControl.InvokeFunc(
                         _pluginName, CreateExternalTerritoryConfig(dontSkipCutscenes)), fallback: false))
            {
                _isExternalControlActivated = true;
                _sentDontSkipCutscenes = dontSkipCutscenes;
            }
        }
        else
        {
            if (_isExternalControlActivated)
            {
                if (IpcInvoke.SafeFunc(() => _disableExternalControl.InvokeFunc(_pluginName), fallback: false) ||
                    IpcInvoke.SafeFunc<bool?>(() => _isInExternalControl.InvokeFunc(), fallback: null) == false)
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
