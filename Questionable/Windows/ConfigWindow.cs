using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using LLib.ImGui;
using Questionable.Windows.ConfigComponents;

namespace Questionable.Windows;

internal sealed class ConfigWindow : LWindow, IPersistableWindowConfig
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly GeneralConfigComponent _generalConfigComponent;
    private readonly PluginConfigComponent _pluginConfigComponent;
    private readonly DutyConfigComponent _dutyConfigComponent;
    private readonly SinglePlayerDutyConfigComponent _singlePlayerDutyConfigComponent;
    private readonly StopConditionComponent _stopConditionComponent;
    private readonly NotificationConfigComponent _notificationConfigComponent;
    private readonly DebugConfigComponent _debugConfigComponent;
    private readonly Configuration _configuration;

    public ConfigWindow(
        IDalamudPluginInterface pluginInterface,
        GeneralConfigComponent generalConfigComponent,
        PluginConfigComponent pluginConfigComponent,
        DutyConfigComponent dutyConfigComponent,
        SinglePlayerDutyConfigComponent singlePlayerDutyConfigComponent,
        StopConditionComponent stopConditionComponent,
        NotificationConfigComponent notificationConfigComponent,
        DebugConfigComponent debugConfigComponent,
        Configuration configuration)
        : base("Config - Questionable###QuestionableConfig", ImGuiWindowFlags.AlwaysAutoResize)
    {
        _pluginInterface = pluginInterface;
        _generalConfigComponent = generalConfigComponent;
        _pluginConfigComponent = pluginConfigComponent;
        _dutyConfigComponent = dutyConfigComponent;
        _singlePlayerDutyConfigComponent = singlePlayerDutyConfigComponent;
        _stopConditionComponent = stopConditionComponent;
        _notificationConfigComponent = notificationConfigComponent;
        _debugConfigComponent = debugConfigComponent;
        _configuration = configuration;
    }

    public WindowConfig WindowConfig => _configuration.ConfigWindowConfig;

    public override void DrawContent()
    {
        using var tabBar = ImRaii.TabBar("QuestionableConfigTabs");
        if (!tabBar)
            return;

        _generalConfigComponent.DrawTab();
        _pluginConfigComponent.DrawTab();
        _dutyConfigComponent.DrawTab();
        _singlePlayerDutyConfigComponent.DrawTab();
        _stopConditionComponent.DrawTab();
        _notificationConfigComponent.DrawTab();
        _debugConfigComponent.DrawTab();
    }

    public void SaveWindowConfig() => _pluginInterface.SavePluginConfig(_configuration);
}
