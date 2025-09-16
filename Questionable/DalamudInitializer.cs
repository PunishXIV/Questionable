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

namespace Questionable;

internal sealed class DalamudInitializer : IDisposable
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IFramework _framework;
    private readonly QuestController _questController;
    private readonly MovementController _movementController;
    private readonly WindowSystem _windowSystem;
    private readonly OneTimeSetupWindow _oneTimeSetupWindow;
    private readonly QuestWindow _questWindow;
    private readonly ConfigWindow _configWindow;
    private readonly IToastGui _toastGui;
    private readonly Configuration _configuration;
    private readonly PartyWatchDog _partyWatchDog;
    private readonly ILogger<DalamudInitializer> _logger;

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
        _toastGui = toastGui;
        _configuration = configuration;
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
        if (_configuration.Advanced.ShowWindowOnStart)
            ToggleQuestWindow();
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
            _questController.Stop("Pathfinding failed");
        }
    }

    private void OnToast(ref SeString message, ref ToastOptions options, ref bool isHandled)
        => _logger.LogTrace("Normal Toast: {Message}", message);

    private void OnErrorToast(ref SeString message, ref bool isHandled)
        => _logger.LogTrace("Error Toast: {Message}", message);

    private void OnQuestToast(ref SeString message, ref QuestToastOptions options, ref bool isHandled)
        => _logger.LogTrace("Quest Toast: {Message}", message);

    private void ToggleQuestWindow()
    {
        if (_configuration.IsPluginSetupComplete())
            _questWindow.ToggleOrUncollapse();
        else
            _oneTimeSetupWindow.IsOpenAndUncollapsed = true;
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
}
