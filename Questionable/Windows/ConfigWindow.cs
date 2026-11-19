namespace Questionable.Windows;

[RegisterSingleton]
internal sealed class ConfigWindow
 : LWindow, IPersistableWindowConfig
{
    private readonly Configuration _configuration;
    private readonly DebugConfigComponent _debugConfigComponent;
    private readonly DutyConfigComponent _dutyConfigComponent;
    private readonly GeneralConfigComponent _generalConfigComponent;
    private readonly PluginConfigComponent _pluginConfigComponent;
    private readonly ProfileController _profileController;
    private readonly ProfilesConfigComponent _profilesConfigComponent;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly StopConditionComponent _stopConditionComponent;
    private readonly NotificationConfigComponent _notificationConfigComponent;
    private readonly QstWidgets.MenuPage[] _pages;

    public ConfigWindow(
    IDalamudPluginInterface pluginInterface,
    GeneralConfigComponent generalConfigComponent,
    PluginConfigComponent pluginConfigComponent,
    ProfilesConfigComponent profilesConfigComponent,
    ProfileController profileController,
    DutyConfigComponent dutyConfigComponent,
    StopConditionComponent stopConditionComponent,
    NotificationConfigComponent notificationConfigComponent,
    DebugConfigComponent debugConfigComponent,
    Configuration configuration) : base(_L("Config - Questionable") + "###QuestionableConfig")
    {
        _configuration = configuration;
        _debugConfigComponent = debugConfigComponent;
        _dutyConfigComponent = dutyConfigComponent;
        _generalConfigComponent = generalConfigComponent;
        _pluginConfigComponent = pluginConfigComponent;
        _profilesConfigComponent = profilesConfigComponent;
        _profileController = profileController;
        _pluginInterface = pluginInterface;
        _stopConditionComponent = stopConditionComponent;
        _notificationConfigComponent = notificationConfigComponent;

        // Page ids are saved as the last open page; don't rename them.
        string playing = _L("Playing"), content = _L("Content"), automation = _L("Automation"), plugin = _L("Plugin");
        _pages =
        [
            new(playing, "General", _L("Basics"), FontAwesomeIcon.SlidersH),
            new(playing, "Questing", _L("Questing"), FontAwesomeIcon.Scroll),
            new(playing, "Rewards", _L("Loot & gear"), FontAwesomeIcon.Coins),
            new(content, "Dungeons", _L("Dungeons & trials"), FontAwesomeIcon.Dragon),
            new(content, "QuestBattles", _L("Solo battles"), FontAwesomeIcon.UserShield),
            new(automation, "Stop", _L("Stopping"), FontAwesomeIcon.HandPaper),
            new(automation, "Notifications", _L("Alerts"), FontAwesomeIcon.Bullhorn),
            new(automation, "Profiles", _L("Profiles"), FontAwesomeIcon.IdCard),
            new(plugin, "Plugins", _L("Helper plugins"), FontAwesomeIcon.PuzzlePiece),
            new(plugin, "Advanced", _L("Expert"), FontAwesomeIcon.Wrench),
        ];

        Size = new Vector2(760, 600);
        SizeCondition = ImGuiCond.Once;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(400, 400),
            MaximumSize = default
        };

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
    }
    public WindowConfig WindowConfig => _configuration.ConfigWindowConfig;

    public void SaveWindowConfig() => _pluginInterface.SavePluginConfig(_configuration);

    // the profiles tab may have switched profiles for editing; go back to the one this character uses
    public override void OnClose() => _profileController.ActivateCharacterProfile();

    public override void DrawContent()
    {
        string page = WindowConfig.Page;
        // Neither page holds profile settings.
        if (_configuration.Profiles.Count > 0 && page is not ("Plugins" or "Profiles"))
        {
            if (_configuration.ActiveProfileName is { } profileName)
                ImGui.TextColored(QstTheme.Amber, _LF("Editing profile '{0}': changes only apply to this profile.", profileName));
            else
                ImGui.TextDisabled(_L("Editing the default settings."));
        }

        using ImRaii.ChildDisposable content = QstWidgets.SideMenu("QuestionableConfig", _pages, ref page);
        if (page != WindowConfig.Page)
        {
            WindowConfig.Page = page;
            SaveWindowConfig();
        }

        if (!content)
            return;

        switch (page)
        {
            case "General": _generalConfigComponent.DrawGeneral(); break;
            case "Questing": _generalConfigComponent.DrawQuestingPage(); break;
            case "Rewards": _generalConfigComponent.DrawRewardsPage(); break;
            case "Dungeons": _dutyConfigComponent.DrawDuties(); break;
            case "QuestBattles": _dutyConfigComponent.DrawQuestBattles(); break;
            case "Stop": _stopConditionComponent.Draw(); break;
            case "Notifications": _notificationConfigComponent.Draw(); break;
            case "Profiles": _profilesConfigComponent.Draw(); break;
            case "Plugins": _pluginConfigComponent.DrawPage(); break;
            case "Advanced": _debugConfigComponent.Draw(); break;
        }
    }
}
