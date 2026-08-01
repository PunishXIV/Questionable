using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Questionable.Windows.Common;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows;

internal sealed class QuestWindow : LWindow, IPersistableWindowConfig
{
    private static readonly Version PluginVersion = typeof(QuestionablePlugin).Assembly.GetName().Version!;
    private readonly ActiveQuestComponent _activeQuestComponent;
    private readonly ARealmRebornComponent _aRealmRebornComponent;
    private readonly IClientState _clientState;
    private readonly Configuration _configuration;
    private readonly CreationUtilsComponent _creationUtilsComponent;
    private readonly EventInfoComponent _eventInfoComponent;
    private readonly IFramework _framework;
    private readonly InteractionUiController _interactionUiController;
    private readonly TitleBarButton _minimizeButton;
    private readonly IObjectTable _objectTable;

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestController _questController;
    private readonly QuickAccessButtonsComponent _quickAccessButtonsComponent;
    private readonly RemainingTasksComponent _remainingTasksComponent;
    private readonly ReportWarningComponent _reportWarningComponent;
    private readonly TerritoryData _territoryData;
    private readonly BossModIpc _bossModIpc;

    private bool _wasRunning;

    public QuestWindow(IDalamudPluginInterface pluginInterface,
        QuestController questController,
        IClientState clientState,
        IObjectTable objectTable,
        Configuration configuration,
        TerritoryData territoryData,
        ActiveQuestComponent activeQuestComponent,
        ARealmRebornComponent aRealmRebornComponent,
        EventInfoComponent eventInfoComponent,
        CreationUtilsComponent creationUtilsComponent,
        QuickAccessButtonsComponent quickAccessButtonsComponent,
        RemainingTasksComponent remainingTasksComponent,
        ReportWarningComponent reportWarningComponent,
        IFramework framework,
        InteractionUiController interactionUiController,
        ConfigWindow configWindow,
        BossModIpc bossModIpc)
        : base((configuration.Advanced.Debug ? "(!) " : "") + $"QST v{PluginVersion.ToString(4)}###Questionable",
            ImGuiWindowFlags.AlwaysAutoResize)
    {
        _pluginInterface = pluginInterface;
        _questController = questController;
        _clientState = clientState;
        _objectTable = objectTable;
        _configuration = configuration;
        _territoryData = territoryData;
        _activeQuestComponent = activeQuestComponent;
        _aRealmRebornComponent = aRealmRebornComponent;
        _eventInfoComponent = eventInfoComponent;
        _creationUtilsComponent = creationUtilsComponent;
        _quickAccessButtonsComponent = quickAccessButtonsComponent;
        _remainingTasksComponent = remainingTasksComponent;
        _reportWarningComponent = reportWarningComponent;
        _framework = framework;
        _interactionUiController = interactionUiController;
        _bossModIpc = bossModIpc;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(300, 30),
            MaximumSize = default
        };
        RespectCloseHotkey = false;
        ShowCloseButton = true;
        AllowClickthrough = false;

        _minimizeButton = new()
        {
            Icon = FontAwesomeIcon.Minus,
            Priority = int.MinValue,
            IconOffset = new(1.5f, 1),
            Click = _ =>
            {
                IsMinimized = !IsMinimized;
                _minimizeButton!.Icon = IsMinimized ? FontAwesomeIcon.WindowMaximize : FontAwesomeIcon.Minus;
            },
            AvailableClickthrough = true
        };
        TitleBarButtons.Insert(0, _minimizeButton);

        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new(1.5f, 1),
            Click = _ => configWindow.IsOpenAndUncollapsed = true,
            Priority = int.MinValue,
            ShowTooltip = () =>
            {
                using ImRaii.TooltipDisposable _ = ImRaii.Tooltip();
                ImGui.Text(_L("Open Configuration"));
            }
        });

        _quickAccessButtonsComponent.Reload += OnReload;
        _questController.IsQuestWindowOpenFunction = () => IsOpen;
    }
    public bool IsMinimized { get; set; }

    public WindowConfig WindowConfig => _configuration.DebugWindowConfig;

    public void SaveWindowConfig() => _pluginInterface.SavePluginConfig(_configuration);

    public override void PreOpenCheck()
    {
        bool isRunning = _questController.IsRunning;

        if (isRunning && !_wasRunning)
            IsOpen = true;
        _wasRunning = isRunning;

        if (isRunning)
            Flags |= ImGuiWindowFlags.NoCollapse;
        else
            Flags &= ~ImGuiWindowFlags.NoCollapse;
    }

    public override bool DrawConditions()
    {
        if (!_configuration.IsPluginSetupComplete())
            return false;

        if (!_clientState.IsLoggedIn || _objectTable[0] == null || _clientState.IsPvPExcludingDen)
            return false;

        if (_configuration.General.HideInAllInstances && _territoryData.IsDutyInstance(_clientState.TerritoryType))
            return false;

        return true;
    }

    public override void DrawContent()
    {
        try
        {
#if REPORTING
            if (!_configuration.General.DismissedReportWarning)
            {
                _reportWarningComponent.Draw();
                ImGui.Separator();
            }
#endif
            string notice = "";
            if (_bossModIpc.BossModRebornDetected())
                notice = _L("Questionable is not compatible with BossMod Reborn!");
            if (notice.Length != 0)
            {
                ImGui.TextColored(QstTheme.Danger, _L("Notice"));
                ImGui.TextWrapped(notice);
                ImGui.Separator();
            }

            _activeQuestComponent.DrawTitleBarPill(WindowName);
            _activeQuestComponent.Draw(IsMinimized);
            if (!IsMinimized)
            {
                if (false)
                {
                    // TODO add tests
                }

                if (_aRealmRebornComponent.ShouldDraw
                    && QstWidgets.SectionHeader(_L("A Realm Reborn"), "ARealmReborn"))
                    _aRealmRebornComponent.Draw();

                if (_eventInfoComponent.ShouldDraw
                    && QstWidgets.SectionHeader(_L("Events"), "Events"))
                    _eventInfoComponent.Draw();

                if (QstWidgets.SectionHeader(_L("Quick Access"), "QuickAccess"))
                    _quickAccessButtonsComponent.Draw();

                if (QstWidgets.SectionHeader(_L("Path Tools"), "PathTools", defaultOpen: false))
                    _creationUtilsComponent.Draw();

                if (!_configuration.General.HideRemainingTasks &&
                        QstWidgets.SectionHeader(_L("Remaining Tasks"), "RemainingTasks", count: _remainingTasksComponent.Tasks.Count))
                    _remainingTasksComponent.Draw();
            }
        }
        catch (Exception e)
        {
            ImGui.TextColored(QstTheme.Danger, e.ToString());
        }
    }

    private void OnReload(object? sender, EventArgs e) => Reload();

    internal void Reload()
    {
        _questController.Reload();
        _ = _framework.RunOnTick(_interactionUiController.HandleCurrentDialogueChoices,
            TimeSpan.FromMilliseconds(200));
    }
}
