using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using LLib.ImGui;
using Microsoft.Extensions.Logging;
using Questionable.Windows.ConfigComponents;

namespace Questionable.Windows;

internal sealed class OneTimeSetupWindow : LWindow
{
    private readonly PluginConfigComponent _pluginConfigComponent;
    private readonly Configuration _configuration;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ILogger<OneTimeSetupWindow> _logger;

    public OneTimeSetupWindow(
        PluginConfigComponent pluginConfigComponent,
        Configuration configuration,
        IDalamudPluginInterface pluginInterface,
        ILogger<OneTimeSetupWindow> logger)
        : base("Questionable Setup###QuestionableOneTimeSetup",
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings, true)
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
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen))
            {
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Check, "Finish Setup"))
                {
                    _logger.LogInformation("Marking setup as complete");
                    _configuration.MarkPluginSetupComplete();
                    _pluginInterface.SavePluginConfig(_configuration);
                    IsOpen = false;
                }
            }
        }
        else
        {
            using (ImRaii.Disabled())
            {
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
                    ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Check, "Missing required plugins");
            }
        }

        ImGui.SameLine();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Times, "Close window & don't enable Questionable"))
        {
            _logger.LogWarning("Closing window without all required plugins installed");
            IsOpen = false;
        }
    }
}
