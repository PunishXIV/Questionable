using Dalamud.Utility;
namespace Questionable.Windows.ConfigComponents;

[RegisterSingleton]
internal sealed class NotificationConfigComponent
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    NotificationMasterIpc notificationMasterIpc) : ConfigComponent(pluginInterface, configuration)
{

    public void Draw()
    {
        using QstWidgets.CardScope card = QstWidgets.TitledCard(_L("Alerts"));
        bool enabled = Configuration.Notifications.Enabled;
        if (ImGui.Checkbox(_L("Use alerts"), ref enabled))
        {
            Configuration.Notifications.Enabled = enabled;
            Save();
        }

        using (ImRaii.Disabled(!Configuration.Notifications.Enabled))
        {
            using (ImRaii.PushIndent())
            {
                ImGui.TextUnformatted(_L("Alert me when"));
                using (ImRaii.PushIndent())
                {
                    ImGui.TextDisabled(_L("Questionable needs you for a step (always)"));
                    DrawCheckbox(_L("A stop condition is reached"),
                        () => Configuration.Notifications.NotifyOnStopCondition, x => Configuration.Notifications.NotifyOnStopCondition = x);
                    DrawCheckbox(_L("Questionable can't continue"),
                        () => Configuration.Notifications.NotifyOnCriticalFailure, x => Configuration.Notifications.NotifyOnCriticalFailure = x);
                    DrawCheckbox(_L("You or another plugin stops it"),
                        () => Configuration.Notifications.NotifyOnRequestedStop, x => Configuration.Notifications.NotifyOnRequestedStop = x,
                        _L("/qst stop, or a plugin such as Automaton or a Something Need Doing (SND) script. The Stop button never alerts."));
                }

                ImGui.Spacing();
                XivChatType[] chatTypes = Enum.GetValues<XivChatType>()
                    .Where(x => x != XivChatType.StandardEmote)
                    .ToArray();
                string[] chatTypeNames = chatTypes
                    .Select(t => t.GetAttribute<XivChatTypeInfoAttribute>()?.FancyName ?? t.ToString())
                    .ToArray();
                DrawComboOption(_L("Chat channel"), chatTypes, chatTypeNames,
                    () => Configuration.Notifications.ChatType,
                    v => Configuration.Notifications.ChatType = v);
                ImGuiComponents.HelpMarker(_L("Only for steps that need you. Stop and problem messages always show in Questionable's own chat line."));

                ImGui.Separator();
                ImGui.Text("NotificationMaster");
                ImGuiComponents.HelpMarker(_L("Requires the plugin 'NotificationMaster' to be installed."));
                ImGui.SameLine();
                using (ImRaii.Disabled(!notificationMasterIpc.Enabled))
                {
                    if (ImGuiComponentsLocal.IconButtonWithText(Dalamud.Interface.FontAwesomeIcon.Bell, _L("Test")))
                        notificationMasterIpc.Notify(_L("Test message"));
                    using var nmIndent = ImRaii.PushIndent();
                    string[] sounds = ["", .. GetSounds()];
                    string[] soundNames = [_L("None"), .. sounds.Skip(1).Select(Path.GetFileNameWithoutExtension).Cast<string>()];
                    DrawComboOption(_L("Sound"), sounds, soundNames,
                        () => Configuration.Notifications.Sound,
                        v => Configuration.Notifications.Sound = v);
                    ImGui.SameLine();
                    using (ImRaii.Disabled(string.IsNullOrEmpty(Configuration.Notifications.Sound)))
                    {
                        if (ImGuiComponentsLocal.IconButtonWithText(Dalamud.Interface.FontAwesomeIcon.Play, _L("Test")))
                            notificationMasterIpc.PlaySound(Configuration.Notifications.Sound);
                    }

                    bool onlyWhenUnfocused = Configuration.Notifications.OnlyWhenUnfocused;
                    if (ImGui.Checkbox(_L("Only when the game is not focused"), ref onlyWhenUnfocused))
                    {
                        Configuration.Notifications.OnlyWhenUnfocused = onlyWhenUnfocused;
                        Save();
                    }

                    bool showTrayMessage = Configuration.Notifications.ShowTrayMessage;
                    if (ImGui.Checkbox(_L("Show tray notification"), ref showTrayMessage))
                    {
                        Configuration.Notifications.ShowTrayMessage = showTrayMessage;
                        Save();
                    }

                    bool flashTaskbar = Configuration.Notifications.FlashTaskbar;
                    if (ImGui.Checkbox(_L("Flash taskbar icon"), ref flashTaskbar))
                    {
                        Configuration.Notifications.FlashTaskbar = flashTaskbar;
                        Save();
                    }
                }
            }
        }
    }

    private string[]? _sounds;

    private string[] GetSounds()
    {
        try
        {
            return _sounds ??= Directory.GetFiles(NotificationMasterIpc.SoundFolder, "*.wav")
                .Select(Path.GetFileName).Cast<string>().Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (IOException)
        {
            return _sounds = [];
        }
        catch (UnauthorizedAccessException)
        {
            return _sounds = [];
        }
    }
}
