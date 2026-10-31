using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using PunishLib.ImGuiMethods;
using Questionable.Windows.Common;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows;

[RegisterSingleton]
internal sealed class ConfigWindow
 : LWindow, IPersistableWindowConfig
{
    private readonly Configuration _configuration;
    private readonly DebugConfigComponent _debugConfigComponent;
    private readonly DutyConfigComponent _dutyConfigComponent;
    private readonly GeneralConfigComponent _generalConfigComponent;
    private readonly NotificationConfigComponent _notificationConfigComponent;
    private readonly PluginConfigComponent _pluginConfigComponent;
    private readonly ProfileController _profileController;
    private readonly ProfilesConfigComponent _profilesConfigComponent;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly SinglePlayerDutyConfigComponent _singlePlayerDutyConfigComponent;
    private readonly StopConditionComponent _stopConditionComponent;

    public ConfigWindow(
    IDalamudPluginInterface pluginInterface,
    GeneralConfigComponent generalConfigComponent,
    PluginConfigComponent pluginConfigComponent,
    ProfilesConfigComponent profilesConfigComponent,
    ProfileController profileController,
    DutyConfigComponent dutyConfigComponent,
    SinglePlayerDutyConfigComponent singlePlayerDutyConfigComponent,
    StopConditionComponent stopConditionComponent,
    NotificationConfigComponent notificationConfigComponent,
    DebugConfigComponent debugConfigComponent,
    Configuration configuration) : base(_L("Config - Questionable") + "###QuestionableConfig")
    {
        _configuration = configuration;
        _debugConfigComponent = debugConfigComponent;
        _dutyConfigComponent = dutyConfigComponent;
        _generalConfigComponent = generalConfigComponent;
        _notificationConfigComponent = notificationConfigComponent;
        _pluginConfigComponent = pluginConfigComponent;
        _profilesConfigComponent = profilesConfigComponent;
        _profileController = profileController;
        _pluginInterface = pluginInterface;
        _singlePlayerDutyConfigComponent = singlePlayerDutyConfigComponent;
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
    public override void OnClose() => _profileController.ActivateForCurrentCharacter();

    public override void DrawContent()
    {
        if (_configuration.Profiles.Count > 0)
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
        _pluginConfigComponent.DrawTab();
        _dutyConfigComponent.DrawTab();
        _singlePlayerDutyConfigComponent.DrawTab();
        _stopConditionComponent.DrawTab();
        _notificationConfigComponent.DrawTab();
        _debugConfigComponent.DrawTab();
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("About") + "###QuestionableConfigTabs");
        if (!tab)
            return;
        AboutTab.Draw("Questionable");
    }
}
