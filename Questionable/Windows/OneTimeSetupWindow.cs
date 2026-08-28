using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Questionable.Windows.Common;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows;

[RegisterSingleton]
internal sealed class OneTimeSetupWindow : LWindow
{
    private readonly Configuration _configuration;
    private readonly ILogger<OneTimeSetupWindow> _logger;
    private readonly PluginConfigComponent _pluginConfigComponent;
    private readonly IDalamudPluginInterface _pluginInterface;

    public OneTimeSetupWindow(
        PluginConfigComponent pluginConfigComponent,
        Configuration configuration,
        IDalamudPluginInterface pluginInterface,
        ILogger<OneTimeSetupWindow> logger)
        : base(_L("Questionable Setup") + "###QuestionableOneTimeSetup",
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings, forceMainWindow: true)
    {
        _pluginConfigComponent = pluginConfigComponent;
        _configuration = configuration;
        _pluginInterface = pluginInterface;
        _logger = logger;

        RespectCloseHotkey = false;
        ShowCloseButton = false;
        AllowPinning = false;
        AllowClickthrough = false;
        IsOpen = !_configuration.IsPluginSetupComplete();
        _logger.LogInformation("One-time setup needed: {IsOpen}", IsOpen);
    }

    public override void DrawContent()
    {
        _pluginConfigComponent.Draw(out bool allRequiredInstalled);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (allRequiredInstalled)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, QstTheme.Success))
            {
                if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Check, _L("Finish Setup")))
                {
                    _logger.LogInformation("Marking setup as complete");
                    _configuration.MarkPluginSetupComplete();
                    _pluginInterface.SavePluginConfig(_configuration);
                    IsOpen = false;
                }
            }
        }
        else if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Download, _L("Install all required plugins")))
        {
            _pluginConfigComponent.InstallMissingRequiredPlugins();
        }

        ImGui.SameLine();

        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Times, _L("Close window & don't enable Questionable")))
        {
            _logger.LogWarning("Closing window without all required plugins installed");
            IsOpen = false;
        }
    }
}
