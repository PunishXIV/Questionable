using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LLib.ImGui;
using Questionable.Controller;
using Questionable.Controller.GameUi;
using Questionable.Data;
using Questionable.Windows.QuestComponents;
using System;
using System.Numerics;

namespace Questionable.Windows;

internal sealed class QuestWindow : LWindow, IPersistableWindowConfig
{
    private static readonly Version PluginVersion = typeof(QuestionablePlugin).Assembly.GetName().Version!;

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestController _questController;
    private readonly IClientState _clientState;
    private readonly Configuration _configuration;
    private readonly TerritoryData _territoryData;
    private readonly ActiveQuestComponent _activeQuestComponent;
    private readonly ARealmRebornComponent _aRealmRebornComponent;
    private readonly CreationUtilsComponent _creationUtilsComponent;
    private readonly EventInfoComponent _eventInfoComponent;
    private readonly QuickAccessButtonsComponent _quickAccessButtonsComponent;
    private readonly RemainingTasksComponent _remainingTasksComponent;
    private readonly IFramework _framework;
    private readonly InteractionUiController _interactionUiController;
    private readonly TitleBarButton _minimizeButton;

    public QuestWindow(IDalamudPluginInterface pluginInterface,
        QuestController questController,
        IClientState clientState,
        Configuration configuration,
        TerritoryData territoryData,
        ActiveQuestComponent activeQuestComponent,
        ARealmRebornComponent aRealmRebornComponent,
        EventInfoComponent eventInfoComponent,
        CreationUtilsComponent creationUtilsComponent,
        QuickAccessButtonsComponent quickAccessButtonsComponent,
        RemainingTasksComponent remainingTasksComponent,
        IFramework framework,
        InteractionUiController interactionUiController,
        ConfigWindow configWindow)
        : base($"Questionable v{PluginVersion.ToString(4)}###Questionable",
            ImGuiWindowFlags.AlwaysAutoResize)
    {
        _pluginInterface = pluginInterface;
        _questController = questController;
        _clientState = clientState;
        _configuration = configuration;
        _territoryData = territoryData;
        _activeQuestComponent = activeQuestComponent;
        _aRealmRebornComponent = aRealmRebornComponent;
        _eventInfoComponent = eventInfoComponent;
        _creationUtilsComponent = creationUtilsComponent;
        _quickAccessButtonsComponent = quickAccessButtonsComponent;
        _remainingTasksComponent = remainingTasksComponent;
        _framework = framework;
        _interactionUiController = interactionUiController;

#if DEBUG
        IsOpenAndUncollapsed = true;
#endif
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(240, 30),
            MaximumSize = default
        };
        RespectCloseHotkey = false;
        AllowClickthrough = false;

        _minimizeButton = new TitleBarButton
        {
            Icon = FontAwesomeIcon.Minus,
            Priority = int.MinValue,
            IconOffset = new Vector2(1.5f, 1),
            Click = _ =>
            {
                IsMinimized = !IsMinimized;
                _minimizeButton!.Icon = IsMinimized ? FontAwesomeIcon.WindowMaximize : FontAwesomeIcon.Minus;
            },
            AvailableClickthrough = true,
        };
        TitleBarButtons.Insert(0, _minimizeButton);

        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(1.5f, 1),
            Click = _ => configWindow.IsOpenAndUncollapsed = true,
            Priority = int.MinValue,
            ShowTooltip = () =>
            {
                ImGui.BeginTooltip();
                ImGui.Text("Open Configuration");
                ImGui.EndTooltip();
            }
        });

        _activeQuestComponent.Reload += OnReload;
        _quickAccessButtonsComponent.Reload += OnReload;
        _questController.IsQuestWindowOpenFunction = () => IsOpen;
    }

    public WindowConfig WindowConfig => _configuration.DebugWindowConfig;
    public bool IsMinimized { get; set; }

    public void SaveWindowConfig() => _pluginInterface.SavePluginConfig(_configuration);

    public override void PreOpenCheck()
    {
        if (_questController.IsRunning)
        {
            IsOpen = true;
            Flags |= ImGuiWindowFlags.NoCollapse;
            ShowCloseButton = false;
        }
        else
        {
            Flags &= ~ImGuiWindowFlags.NoCollapse;
            ShowCloseButton = true;
        }
    }

    public override bool DrawConditions()
    {
        if (!_configuration.IsPluginSetupComplete())
            return false;

        if (!_clientState.IsLoggedIn || _clientState.LocalPlayer == null || _clientState.IsPvPExcludingDen)
            return false;

        if (_configuration.General.HideInAllInstances && _territoryData.IsDutyInstance(_clientState.TerritoryType))
            return false;

        return true;
    }

    public override void DrawContent()
    {
        try
        {
            _activeQuestComponent.Draw(IsMinimized);
            if (!IsMinimized)
            {
                ImGui.Separator();

                if (_aRealmRebornComponent.ShouldDraw)
                {
                    _aRealmRebornComponent.Draw();
                    ImGui.Separator();
                }

                if (_eventInfoComponent.ShouldDraw)
                {
                    _eventInfoComponent.Draw();
                    ImGui.Separator();
                }

                _creationUtilsComponent.Draw();
                ImGui.Separator();

                _quickAccessButtonsComponent.Draw();
                _remainingTasksComponent.Draw();
            }
        }
        catch (Exception e)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, e.ToString());
        }
    }

    private void OnReload(object? sender, EventArgs e) => Reload();

    internal void Reload()
    {
        _questController.Reload();
        _framework.RunOnTick(() => _interactionUiController.HandleCurrentDialogueChoices(),
            TimeSpan.FromMilliseconds(200));
    }
}
