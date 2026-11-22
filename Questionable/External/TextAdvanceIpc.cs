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
    private readonly ILogger<TextAdvanceIpc> _logger;
    private bool _hasControl;
    private bool _sentDontSkipCutscenes;
    private bool _sentCompleteQuests;

    public TextAdvanceIpc(IDalamudPluginInterface pluginInterface, IFramework framework,
        QuestController questController, Configuration configuration, ILogger<TextAdvanceIpc> logger)
    {
        _logger = logger;
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
        if (_hasControl)
            IpcInvoke.TryOnFrameworkThread(_framework, () => _disableExternalControl(_pluginName));
        IPCSubscriber.DisposeAll(_disposalTokens);
    }

    private void OnUpdate(IFramework framework)
    {
        bool hasActiveQuest = _questController.IsRunning ||
                              _questController.AutomationType != QuestController.EAutomationType.Manual;
        bool dontSkipCutscenes = _configuration.General.DontSkipCutscenes;
        QuestController.QuestProgress? progress = _questController.CurrentQuest;
        bool completeQuests = CompletesQuestHere(progress?.FindStep(),
            progress?.Quest.Info is QuestInfo { CompletesInstantly: true });
        // A missing or unloading TextAdvance must not throw from every framework update.
        if (_configuration.General.ConfigureTextAdvance && hasActiveQuest)
        {
            if (_hasControl && (dontSkipCutscenes != _sentDontSkipCutscenes || completeQuests != _sentCompleteQuests))
            {
                // Keep control throughout, or TextAdvance's own auto-interact/cutscene settings take over.
                if (IpcInvoke.SafeFunc(() => _enableExternalControl(
                        _pluginName, CreateConfig(dontSkipCutscenes, completeQuests)), fallback: false))
                {
                    _sentDontSkipCutscenes = dontSkipCutscenes;
                    _sentCompleteQuests = completeQuests;
                    _logger.LogDebug("Updated TextAdvance settings (skip cutscenes: {Skip}, turn in quests: {TurnIn})",
                        !dontSkipCutscenes, completeQuests);
                }
            }
            else if (IpcInvoke.SafeFunc<bool?>(() => _isInExternalControl(), fallback: null) == false &&
                     IpcInvoke.SafeFunc(() => _enableExternalControl(
                         _pluginName, CreateConfig(dontSkipCutscenes, completeQuests)), fallback: false))
            {
                _hasControl = true;
                _sentDontSkipCutscenes = dontSkipCutscenes;
                _sentCompleteQuests = completeQuests;
                _logger.LogInformation("Took control of TextAdvance (skip cutscenes: {Skip}, turn in quests: {TurnIn})",
                    !dontSkipCutscenes, completeQuests);
            }
        }
        else
        {
            if (_hasControl)
            {
                if (IpcInvoke.SafeFunc(() => _disableExternalControl(_pluginName), fallback: false) ||
                    IpcInvoke.SafeFunc<bool?>(() => _isInExternalControl(), fallback: null) == false)
                {
                    _hasControl = false;
                    _logger.LogInformation("Gave TextAdvance control back");
                }
            }
        }
    }

    // Talking to a giver for a middle step would otherwise turn in another finished quest of theirs.
    internal static bool CompletesQuestHere(QuestStep? step, bool completesInstantly) =>
        step == null || step.InteractionType == EInteractionType.CompleteQuest || step.TurnInQuestId != null ||
        (step.InteractionType == EInteractionType.AcceptQuest && completesInstantly);

    private static ExternalTerritoryConfig CreateConfig(bool dontSkipCutscenes, bool completeQuests) => new()
    {
        EnableQuestAccept = true,
        EnableQuestComplete = completeQuests,
        EnableRewardPick = false,
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
