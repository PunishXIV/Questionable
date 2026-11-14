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
    private bool _hideProfileBanner;

    public ConfigWindow(
    IDalamudPluginInterface pluginInterface,
    GeneralConfigComponent generalConfigComponent,
    PluginConfigComponent pluginConfigComponent,
    ProfilesConfigComponent profilesConfigComponent,
    ProfileController profileController,
    DutyConfigComponent dutyConfigComponent,
    StopConditionComponent stopConditionComponent,
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

        Size = new Vector2(400, 400);
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
        if (_configuration.Profiles.Count > 0 && !_hideProfileBanner)
        {
            if (_configuration.ActiveProfileName is { } profileName)
                ImGui.TextColored(QstTheme.Amber, _LF("Editing profile '{0}': changes only apply to this profile.", profileName));
            else
                ImGui.TextDisabled(_L("Editing the default settings."));
        }

        using ImRaii.TabBarDisposable tabBar = ImRaii.TabBar("QuestionableConfigTabs");
        if (!tabBar)
            return;

        _generalConfigComponent.DrawTab();
        _profilesConfigComponent.DrawTab();
        _dutyConfigComponent.DrawTab();
        _stopConditionComponent.DrawTab();
        _pluginConfigComponent.DrawTab();
        _debugConfigComponent.DrawTab();

        // Neither tab holds profile settings. The tab is only known after drawing, so this lags one frame.
        _hideProfileBanner = _pluginConfigComponent.IsOpen || _profilesConfigComponent.IsOpen;
    }
}
