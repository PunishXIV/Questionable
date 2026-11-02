using static Questionable.External.IPCUtils;
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
namespace Questionable.External;

[RegisterSingleton]
internal sealed class YesAlreadyIpc : Ipc, IDisposable
{
    public override string InternalName => "YesAlready";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() =>
        {
            var _ = IsPluginEnabled;
            return GetVersion() != null;
        }, fallback: false);
    private static readonly EzIPCDisposalToken[] _disposalTokens = EzIPC.Init(typeof(YesAlreadyIpc), "YesAlready", SafeWrapper.IPCException);
    [EzIPC("IsPluginEnabled")] public static readonly Func<bool> IsPluginEnabled;
    [EzIPC("SetPluginEnabled")] private static readonly Action<bool> SetPluginEnabled;
    private readonly IClientState _clientState;

    private readonly IFramework _framework;
    private readonly ILogger<YesAlreadyIpc> _logger;
    private readonly QuestController _questController;
    private readonly TerritoryData _territoryData;

    // Only true while YesAlready is off because Questionable turned it off; the user's own state is never restored over.
    private bool _disabledByQuestionable;

    public YesAlreadyIpc(IFramework framework,
        QuestController questController,
        TerritoryData territoryData,
        IClientState clientState,
        ILogger<YesAlreadyIpc> logger)
    {
        _framework = framework;
        _questController = questController;
        _territoryData = territoryData;
        _clientState = clientState;
        _logger = logger;

        _framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        IpcInvoke.TryOnFrameworkThread(_framework, () =>
        {
            if (IPCSubscriber.IsInstalled(InternalName) && _disabledByQuestionable && !IsPluginEnabled())
            {
                _logger.LogDebug("Re-enabling YesAlready on dispose");
                SetPluginEnabled(true);
            }

            _disabledByQuestionable = false;
            IPCSubscriber.DisposeAll(_disposalTokens);
        }, _logger);
    }

    private void OnUpdate(IFramework framework)
    {
        if (!IPCSubscriber.IsInstalled(InternalName))
            return;

        bool hasActiveQuest = (_questController.IsRunning ||
                               _questController.AutomationType != QuestController.EAutomationType.Manual) &&
                              !_territoryData.IsDutyInstance(_clientState.TerritoryType);
        (bool? setEnabled, _disabledByQuestionable) =
            DecideToggle(hasActiveQuest, IsPluginEnabled(), _disabledByQuestionable);
        if (setEnabled is { } enabled)
        {
            _logger.LogDebug("Requested YesAlready {State}", enabled ? "on" : "off");
            SetPluginEnabled(enabled);
        }
    }

    /// <returns>The state to request, or null to leave YesAlready alone, and whether Questionable now owns it being off.</returns>
    internal static (bool? SetEnabled, bool DisabledByQuestionable) DecideToggle(bool hasActiveQuest, bool isEnabled,
        bool disabledByQuestionable)
    {
        if (hasActiveQuest)
        {
            // A user re-enabling YesAlready mid-run is respected until the run ends.
            if (isEnabled && !disabledByQuestionable)
                return (false, true);
            return (null, disabledByQuestionable);
        }

        if (disabledByQuestionable && !isEnabled)
            return (true, false);
        return (null, false);
    }
}
