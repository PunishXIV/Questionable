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
        PathEditorWindow pathEditorWindow,
        QuestReportsWindow questReportsWindow,
        WhatsNewWindow whatsNewWindow,
        IChatGui chatGui,
        IToastGui toastGui,
        Configuration configuration,
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
        _windowSystem.AddWindow(pathEditorWindow);
        _windowSystem.AddWindow(questReportsWindow);
        _windowSystem.AddWindow(whatsNewWindow);

        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenMainUi += ToggleQuestWindow;
        _pluginInterface.UiBuilder.OpenConfigUi += _configWindow.Toggle;
        _framework.Update += FrameworkUpdate;
        _toastGui.Toast += OnToast;
        _toastGui.ErrorToast += OnErrorToast;
        _toastGui.QuestToast += OnQuestToast;
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
            string text = GetPathfindingError(e.Reason);
            if (IsPlayerCaused(e.Reason))
                _chatGui.Print(text, CommandHandler.MessageTag, CommandHandler.TagColor);
            else
                _chatGui.PrintError(text, CommandHandler.MessageTag, CommandHandler.TagColor);
            _questController.Stop(_L("Pathfinding failed"));
        }
    }

    // Movement another plugin is doing isn't a failed path.
    internal static bool IsPlayerCaused(MovementController.PathfindingFailureReason reason) =>
        reason is MovementController.PathfindingFailureReason.MovementBlocked;

    internal static string GetPathfindingError(MovementController.PathfindingFailureReason reason) => reason switch
    {
        MovementController.PathfindingFailureReason.MovementBlocked =>
            _L("Navigation stopped because another plugin is moving your character. Wait for it to finish before resuming QST."),
        MovementController.PathfindingFailureReason.Unavailable =>
            _L("Navigation stopped because MappingTheRealm is not available. Install or enable it on the Plugins page in settings, then resume QST."),
        MovementController.PathfindingFailureReason.Cancelled =>
            _L("Navigation stopped because MappingTheRealm cancelled the route. Resume QST when you're ready."),
        _ => _L("MappingTheRealm couldn't finish the route. Use Stuck? to copy troubleshooting information if this keeps happening."),
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
