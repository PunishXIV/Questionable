using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using PunishLib.ImGuiMethods;
using Questionable.Windows.Common;
namespace Questionable.Windows;

internal sealed class ConfigWindow
(
    IDalamudPluginInterface pluginInterface,
    GeneralConfigComponent generalConfigComponent,
    PluginConfigComponent pluginConfigComponent,
    DutyConfigComponent dutyConfigComponent,
    SinglePlayerDutyConfigComponent singlePlayerDutyConfigComponent,
    StopConditionComponent stopConditionComponent,
    NotificationConfigComponent notificationConfigComponent,
    DebugConfigComponent debugConfigComponent,
    Configuration configuration) : LWindow(_L("Config - Questionable") + "###QuestionableConfig"), IPersistableWindowConfig
{
    private readonly Configuration _configuration = configuration;
    private readonly DebugConfigComponent _debugConfigComponent = debugConfigComponent;
    private readonly DutyConfigComponent _dutyConfigComponent = dutyConfigComponent;
    private readonly GeneralConfigComponent _generalConfigComponent = generalConfigComponent;
    private readonly NotificationConfigComponent _notificationConfigComponent = notificationConfigComponent;
    private readonly PluginConfigComponent _pluginConfigComponent = pluginConfigComponent;
    private readonly IDalamudPluginInterface _pluginInterface = pluginInterface;
    private readonly SinglePlayerDutyConfigComponent _singlePlayerDutyConfigComponent = singlePlayerDutyConfigComponent;
    private readonly StopConditionComponent _stopConditionComponent = stopConditionComponent;

    public WindowConfig WindowConfig => _configuration.ConfigWindowConfig;

    public void SaveWindowConfig() => _pluginInterface.SavePluginConfig(_configuration);

    public override void DrawContent()
    {
        using ImRaii.TabBarDisposable tabBar = ImRaii.TabBar("QuestionableConfigTabs");
        if (!tabBar)
            return;
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
                IconOffset = new(1.5f, 1),
                Click = _ => Process.Start(new ProcessStartInfo { FileName = "https://github.com/sponsors/alydevs", UseShellExecute = true }),
                Priority = int.MinValue,
                ShowTooltip = () =>
                {
                    using ImRaii.TooltipDisposable _ = ImRaii.Tooltip();
                    ImGui.Text(_L("Sponsor QST development"));
                }
            });

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
