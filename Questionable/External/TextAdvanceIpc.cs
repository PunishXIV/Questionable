using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class TextAdvanceIpc : Ipc, IDisposable
{
    public override string InternalName => "TextAdvance";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() =>
        {
            var _ = _isInExternalControl();
            return GetVersion() != null;
        }, fallback: false);
    private readonly Configuration _configuration;
    [EzIPC("DisableExternalControl")] private readonly Func<string, bool> _disableExternalControl = null!;
    [EzIPC("EnableExternalControl")] private readonly Func<string, ExternalTerritoryConfig, bool> _enableExternalControl = null!;
    private readonly IFramework _framework;
    [EzIPC("IsInExternalControl")] private readonly Func<bool> _isInExternalControl = null!;
    private readonly EzIPCDisposalToken[] _disposalTokens;
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
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
        _pluginName = pluginInterface.InternalName;
        _framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        if (_isExternalControlActivated)
            IpcInvoke.TryOnFrameworkThread(_framework, () => _disableExternalControl(_pluginName));
        IPCSubscriber.DisposeAll(_disposalTokens);
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
                // TextAdvance accepts updated settings from the same requester. Keep control
                // throughout so its normal auto-interact/cutscene settings cannot take over.
                if (IpcInvoke.SafeFunc(() => _enableExternalControl(
                        _pluginName, CreateExternalTerritoryConfig(dontSkipCutscenes)), fallback: false))
                    _sentDontSkipCutscenes = dontSkipCutscenes;
            }
            else if (IpcInvoke.SafeFunc<bool?>(() => _isInExternalControl(), fallback: null) == false &&
                     IpcInvoke.SafeFunc(() => _enableExternalControl(
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
                if (IpcInvoke.SafeFunc(() => _disableExternalControl(_pluginName), fallback: false) ||
                    IpcInvoke.SafeFunc<bool?>(() => _isInExternalControl(), fallback: null) == false)
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

    internal sealed class ExternalTerritoryConfig
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
