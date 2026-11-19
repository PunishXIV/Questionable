namespace Questionable.Windows;

[RegisterSingleton]
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
    private bool _isMinimized;
    private readonly IObjectTable _objectTable;

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestController _questController;
    private readonly QuickAccessComponent _quickAccessComponent;
    private readonly RemainingTasksComponent _remainingTasksComponent;
    private readonly ReportWarningComponent _reportWarningComponent;
    private readonly TerritoryData _territoryData;
    private readonly BossModIpc _bossModIpc;

    private readonly QueueComponent _queueComponent;
    private bool _wasRunning;
    private bool _selectQueue;

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
        QuickAccessComponent quickAccessComponent,
        RemainingTasksComponent remainingTasksComponent,
        ReportWarningComponent reportWarningComponent,
        IFramework framework,
        InteractionUiController interactionUiController,
        ConfigWindow configWindow,
        BossModIpc bossModIpc,
        QueueComponent queueComponent)
        : base((configuration.Advanced.Debug ? "(!) " : "") + $"QST v{PluginVersion.ToString(4)}###Questionable",
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
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
        _quickAccessComponent = quickAccessComponent;
        _remainingTasksComponent = remainingTasksComponent;
        _reportWarningComponent = reportWarningComponent;
        _framework = framework;
        _interactionUiController = interactionUiController;
        _bossModIpc = bossModIpc;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(320, 30),
            MaximumSize = default
        };
        RespectCloseHotkey = false;
        ShowCloseButton = true;
        AllowClickthrough = false;

        _minimizeButton = new()
        {
            Icon = FontAwesomeIcon.Minus,
            Priority = TitleBarButtonPriority,
            IconOffset = TitleBarIconOffset,
            Click = _ =>
            {
                IsOpenAndUncollapsed = true;
                IsMinimized = !IsMinimized;
            },
            AvailableClickthrough = true
        };
        TitleBarButtons.Add(_minimizeButton);

        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = TitleBarIconOffset,
            Click = _ => configWindow.IsOpenAndUncollapsed = true,
            Priority = TitleBarButtonPriority,
            ShowTooltip = () =>
            {
                using ImRaii.TooltipDisposable _ = ImRaii.Tooltip();
                ImGui.Text(_L("Open Configuration"));
            }
        });

        if (!_configuration.General.HideSponsorButton)
            TitleBarButtons.Add(new()
            {
                Icon = FontAwesomeIcon.Heart,
                IconOffset = TitleBarIconOffset,
                Click = _ => Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/alydev", UseShellExecute = true }),
                Priority = TitleBarButtonPriority,
                ShowTooltip = () =>
                {
                    using ImRaii.TooltipDisposable _ = ImRaii.Tooltip();
                    ImGui.Text(_L("Sponsor QST development"));
                }
            });

        _queueComponent = queueComponent;
        _queueComponent.OpenRequested += OnQueueRequested;
        _quickAccessComponent.Reload += OnReload;
        _questController.IsQuestWindowOpenFunction = () => IsOpen;
    }
    // Start minimized sets this directly, so the button icon follows the property, not the click.
    public bool IsMinimized
    {
        get => _isMinimized;
        set
        {
            _isMinimized = value;
            _minimizeButton.Icon = value ? FontAwesomeIcon.WindowMaximize : FontAwesomeIcon.Minus;
        }
    }

    public WindowConfig WindowConfig => _configuration.DebugWindowConfig;

    public void SaveWindowConfig() => _pluginInterface.SavePluginConfig(_configuration);

    public override void PreOpenCheck()
    {
        bool isRunning = _questController.IsRunning;

        if (isRunning && !_wasRunning)
            IsOpen = true;
        _wasRunning = isRunning;
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
            if (_configuration.Reports.Share == null)
            {
                _reportWarningComponent.Draw();
                ImGui.Separator();
            }

            string notice = "";
            if (_bossModIpc.IsRebornInstalled())
                notice = _L("Questionable is not compatible with Boss Mod Reborn!");
            if (notice.Length != 0)
            {
                ImGui.TextColored(QstTheme.Danger, _L("Notice"));
                ImGui.TextWrapped(notice);
                ImGui.Separator();
            }

            _activeQuestComponent.DrawTitleBarPill(WindowName);
            if (IsMinimized)
            {
                _activeQuestComponent.Draw(true);
                return;
            }

            using ImRaii.TabBarDisposable tabs = ImRaii.TabBar("QuestWindowTabs");
            if (!tabs)
                return;

            using (ImRaii.TabItemDisposable now = ImRaii.TabItem(_L("Now") + "###Now"))
            {
                if (now)
                    DrawNow();
            }

            using ImRaii.TabItemDisposable upNext = ImRaii.TabItem(_L("Priority Quests") + "###UpNext",
                _selectQueue ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None);
            _selectQueue = false;
            if (upNext)
                _queueComponent.Draw();
        }
        catch (Exception e)
        {
            ImGui.TextColored(QstTheme.Danger, e.ToString());
        }
    }

    private void DrawNow()
    {
        _activeQuestComponent.Draw(false);
        _quickAccessComponent.Draw();

        if (_aRealmRebornComponent.ShouldDraw
            && QstWidgets.SectionHeader(_L("A Realm Reborn"), "ARealmReborn"))
            _aRealmRebornComponent.Draw();

        if (_eventInfoComponent.ShouldDraw
            && QstWidgets.SectionHeader(_L("Events"), "Events"))
            _eventInfoComponent.Draw();

        if (!_configuration.General.HideRemainingTasks &&
                QstWidgets.SectionHeader(_L("Remaining Tasks"), "RemainingTasks", count: _remainingTasksComponent.Tasks.Count))
            _remainingTasksComponent.Draw();

        if (QstWidgets.SectionHeader(_L("Target Tools"), "PathTools", defaultOpen: false))
            _creationUtilsComponent.Draw();
    }

    private void OnQueueRequested(object? sender, EventArgs e)
    {
        IsOpenAndUncollapsed = true;
        IsMinimized = false;
        _selectQueue = true;
    }

    private void OnReload(object? sender, EventArgs e) => Reload();

    internal void Reload()
    {
        _questController.Reload();
        _ = _framework.RunOnTick(_interactionUiController.CheckOpenDialogues,
            TimeSpan.FromMilliseconds(200));
    }
}
