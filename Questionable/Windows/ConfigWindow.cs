using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using PunishLib.ImGuiMethods;
using Questionable.Windows.Common;
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
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly SinglePlayerDutyConfigComponent _singlePlayerDutyConfigComponent;
    private readonly StopConditionComponent _stopConditionComponent;

    public ConfigWindow(
    IDalamudPluginInterface pluginInterface,
    GeneralConfigComponent generalConfigComponent,
    PluginConfigComponent pluginConfigComponent,
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

    public override void DrawContent()
    {
        using ImRaii.TabBarDisposable tabBar = ImRaii.TabBar("QuestionableConfigTabs");
        if (!tabBar)
            return;

        _generalConfigComponent.DrawTab();
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
