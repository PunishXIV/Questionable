using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Questionable.Windows.Common;
using Questionable.Windows.ConfigComponents;
using PunishLib.ImGuiMethods;
using static Questionable.Utils.LocalizeShortcut;
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
    Configuration configuration) : LWindow(_L("Config - Questionable") + "###QuestionableConfig", ImGuiWindowFlags.AlwaysAutoResize), IPersistableWindowConfig
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

        _generalConfigComponent.DrawTab();
        _pluginConfigComponent.DrawTab();
        _dutyConfigComponent.DrawTab();
        _singlePlayerDutyConfigComponent.DrawTab();
        _stopConditionComponent.DrawTab();
        _notificationConfigComponent.DrawTab();
        _debugConfigComponent.DrawTab();
        using (ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("About") + "###QuestionableConfigTabs"))
        {
            if (!tab)
                return;
            AboutTab.Draw("Questionable");
        }
    }
}
