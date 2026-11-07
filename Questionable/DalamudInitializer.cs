using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text.SeStringHandling;
using I18N.DotNet;
using Lumina.Excel.Sheets;
namespace Questionable;

[RegisterSingleton]
internal sealed class DalamudInitializer : IDisposable
{
    private readonly Configuration _configuration;
    private readonly ConfigurationLocalization _configurationLocalization;
    private readonly ConfigWindow _configWindow;
    private readonly IFramework _framework;
    private readonly HighlightObject _highlightObject;
    private readonly ILogger<DalamudInitializer> _logger;
    private readonly MovementController _movementController;
    private readonly OneTimeSetupWindow _oneTimeSetupWindow;
    private readonly PartyWatchDog _partyWatchDog;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestController _questController;
    private readonly QuestWindow _questWindow;
    private readonly IChatGui _chatGui;
    private readonly IToastGui _toastGui;
    private readonly WindowSystem _windowSystem;
    private bool _disposed;
    internal bool FlagGearUnequippable;
    internal bool FlagUnableExecuteCommand;

    public DalamudInitializer(
        IDalamudPluginInterface pluginInterface,
        IFramework framework,
        QuestController questController,
        MovementController movementController,
        WindowSystem windowSystem,
        OneTimeSetupWindow oneTimeSetupWindow,
        QuestWindow questWindow,
        DebugOverlay debugOverlay,
        ConfigWindow configWindow,
        QuestSelectionWindow questSelectionWindow,
        QuestValidationWindow questValidationWindow,
        JournalProgressWindow journalProgressWindow,
        PriorityWindow priorityWindow,
        PathEditorWindow pathEditorWindow,
        IChatGui chatGui,
        IToastGui toastGui,
        Configuration configuration,
        HighlightObject highlightObject,
        PartyWatchDog partyWatchDog,
        ILogger<DalamudInitializer> logger)
    {
        _pluginInterface = pluginInterface;
        _framework = framework;
        _questController = questController;
        _movementController = movementController;
        _windowSystem = windowSystem;
        _oneTimeSetupWindow = oneTimeSetupWindow;
        _questWindow = questWindow;
        _configWindow = configWindow;
        _chatGui = chatGui;
        _toastGui = toastGui;
        _configuration = configuration;
        _highlightObject = highlightObject;
        _partyWatchDog = partyWatchDog;
        _logger = logger;
        _configurationLocalization = new(configuration, SetupI18N);

        _windowSystem.AddWindow(oneTimeSetupWindow);
        _windowSystem.AddWindow(questWindow);
        _windowSystem.AddWindow(configWindow);
        _windowSystem.AddWindow(debugOverlay);
        _windowSystem.AddWindow(questSelectionWindow);
        _windowSystem.AddWindow(questValidationWindow);
        _windowSystem.AddWindow(journalProgressWindow);
        _windowSystem.AddWindow(priorityWindow);
        _windowSystem.AddWindow(pathEditorWindow);

        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenMainUi += ToggleQuestWindow;
        _pluginInterface.UiBuilder.OpenConfigUi += _configWindow.Toggle;
        _framework.Update += FrameworkUpdate;
        _toastGui.Toast += OnToast;
        _toastGui.ErrorToast += OnErrorToast;
        _toastGui.QuestToast += OnQuestToast;
        _configuration.Advanced.AbandonQuestBeforeCompletion = false;
        _configuration.Advanced.RemoveFromPriorityWhenAbandoned = false;
        if (_configuration.Advanced.StartMinimized)
            _questWindow.IsMinimized = true;

        if (_configuration.Advanced.ShowWindowOnStart)
            ToggleQuestWindow();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _configurationLocalization.Dispose();

        _toastGui.QuestToast -= OnQuestToast;
        _toastGui.ErrorToast -= OnErrorToast;
        _toastGui.Toast -= OnToast;
        _framework.Update -= FrameworkUpdate;
        _pluginInterface.UiBuilder.OpenConfigUi -= _configWindow.Toggle;
        _pluginInterface.UiBuilder.OpenMainUi -= ToggleQuestWindow;
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;

        _windowSystem.RemoveAllWindows();
    }

    private void FrameworkUpdate(IFramework framework)
    {
        _partyWatchDog.Update();
        _questController.Update();

        try
        {
            _movementController.Update();
        }
        catch (MovementController.PathfindingException e)
        {
            _questController.RecordNavigationFailure(e);
            _logger.LogError(e, "Navigation failed ({Reason}), quest {Quest}, NeverFly:{NeverFly}",
                e.Reason, _questController.CurrentQuest?.ToString(), _configuration.Advanced.NeverFly);
            _chatGui.PrintError(GetPathfindingFailureMessage(e.Reason, _configuration.Advanced.NeverFly),
                CommandHandler.MessageTag, CommandHandler.TagColor);
            _questController.Stop(_L("Pathfinding failed"));
        }
    }

    internal static string GetPathfindingFailureMessage(MovementController.PathfindingFailureReason reason, bool neverFly) => reason switch
    {
        MovementController.PathfindingFailureReason.MovementDisabled =>
            _L("Navigation stopped because movement is disabled in vnavmesh. Re-enable it when you are ready, then resume QST."),
        MovementController.PathfindingFailureReason.StatusUnavailable =>
            _L("Navigation stopped because vnavmesh's movement status could not be checked. Check that vnavmesh is loaded before resuming QST."),
        MovementController.PathfindingFailureReason.StartFailed =>
            _L("Navigation could not be started. Check /xllog for the underlying error before resuming QST."),
        MovementController.PathfindingFailureReason.NotArrived =>
            _L("Movement stopped before reaching the destination. Use Stuck? to copy troubleshooting information if this keeps happening."),
        MovementController.PathfindingFailureReason.MovementBlocked =>
            _L("Navigation stopped because another plugin is travelling or vnavmesh has a pending movement request. Wait for that activity to finish before resuming QST."),
        MovementController.PathfindingFailureReason.Unavailable =>
            _L("Navigation stopped because vnavmesh's IPC is unavailable. Check that vnavmesh is loaded, then resume QST."),
        MovementController.PathfindingFailureReason.TimedOut =>
            _L("Navigation stopped because pathfinding exceeded 30 seconds. If this keeps happening, use Stuck? to copy troubleshooting information and include the /xllog error."),
        MovementController.PathfindingFailureReason.Cancelled =>
            _L("Navigation stopped because vnavmesh cancelled the pathfinding request. Resume QST when navigation is available."),
        MovementController.PathfindingFailureReason.NoRoute when neverFly =>
            _L("vnavmesh returned no route. Flying is disabled in QST config > Advanced; enable it if this route requires flying."),
        MovementController.PathfindingFailureReason.NoRoute =>
            _L("vnavmesh returned no route from your current position. If this keeps happening, use Stuck? to copy troubleshooting information and describe where you became stuck."),
        MovementController.PathfindingFailureReason.LandingFailed =>
            _L("Navigation stopped because landing did not complete. Land in a suitable place, then resume QST."),
        _ => _L("Navigation failed. Check /xllog for the underlying error; include it with the Stuck? troubleshooting information if you report the problem."),
    };

    private void OnToast(ref SeString message, ref ToastOptions options, ref bool isHandled) => _logger.LogTrace("Normal Toast: {Message}", message);

    private void OnErrorToast(ref SeString message, ref bool isHandled)
    {
        _logger.LogDebug("Error toast: {Message}", message.TextValue);
        if (DialogueReferenceResolver.IsMatch(
                message.ToString(),
                new(Svc.Data.GetExcelSheet<LogMessage>().GetRow(1951).Text.ToRegex())) // 'contains gear unequippable'
            && _questController.IsRunning)
        {
            FlagGearUnequippable = true;
            _logger.LogDebug("Toast: Gear Unequippable");
        }
        else if (FlagGearUnequippable)
        {
            FlagGearUnequippable = false;
        }

        if (DialogueReferenceResolver.IsMatch(
                message.ToString(),
                new(Svc.Data.GetExcelSheet<LogMessage>().GetRow(7777).Text.ToRegex())) // 'unable to execute command while in flight'
            && _questController.IsRunning)
        {
            FlagUnableExecuteCommand = true;
            _logger.LogDebug("Toast: UnableExecuteCommand");
        }
        else if (FlagUnableExecuteCommand)
        {
            FlagUnableExecuteCommand = false;
        }

        _logger.LogTrace("Error Toast: {Message}", message);
    }

    private void OnQuestToast(ref SeString message, ref QuestToastOptions options, ref bool isHandled) => _logger.LogTrace("Quest Toast: {Message}", message);

    private void ToggleQuestWindow()
    {
        if (_configuration.IsPluginSetupComplete())
        {
            _questWindow.ToggleOrUncollapse();
        }
        else
        {
            _oneTimeSetupWindow.IsOpenAndUncollapsed = true;
        }
    }

    internal static void SetupI18N(CultureInfo culture) => GlobalLocalizer.Localizer.Load(culture);
    internal static void SetupI18N(string language)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        GlobalLocalizer.Localizer.LoadXML(
            Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory?.FullName ??
                new FileInfo(typeof(DalamudInitializer).Assembly.Location).DirectoryName ?? "", "Resources", "I18N.xml"),
            CultureInfo.CurrentUICulture);
    }
}
