using System;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Questionable.Controller;
using Questionable.Controller.Utils;
using Questionable.Windows;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable;

internal sealed class DalamudInitializer : IDisposable
{
    private readonly Configuration _configuration;
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

        _windowSystem.AddWindow(oneTimeSetupWindow);
        _windowSystem.AddWindow(questWindow);
        _windowSystem.AddWindow(configWindow);
        _windowSystem.AddWindow(debugOverlay);
        _windowSystem.AddWindow(questSelectionWindow);
        _windowSystem.AddWindow(questValidationWindow);
        _windowSystem.AddWindow(journalProgressWindow);
        _windowSystem.AddWindow(priorityWindow);

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
        catch (MovementController.PathfindingFailedException)
        {
            if (_configuration.Advanced.NeverFly)
                _chatGui.PrintError(_L("vnavmesh was not able to find a path. This may be due to the " +
                    "'Disable flying' setting in QST config > Advanced. Please uncheck this if you expected this " +
                    "to run fine."), CommandHandler.MessageTag, CommandHandler.TagColor);
            else
                _chatGui.PrintError(_LF("vnavmesh was not able to find a path! Please report this to " +
                    "Questionable developers. {0}", _questController.CurrentQuest?.ToString() ?? ""), CommandHandler.MessageTag, CommandHandler.TagColor);
            _questController.Stop(_L("Pathfinding failed"));
        }
    }

    private void OnToast(ref SeString message, ref ToastOptions options, ref bool isHandled) => _logger.LogTrace("Normal Toast: {Message}", message);

    private void OnErrorToast(ref SeString message, ref bool isHandled) => _logger.LogTrace("Error Toast: {Message}", message);

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
}
