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
        if (ImGui.Checkbox(_L("Enable notifications when manual interaction is required"), ref enabled))
        {
            Configuration.Notifications.Enabled = enabled;
            Save();
        }

        using (ImRaii.Disabled(!Configuration.Notifications.Enabled))
        {
            using (ImRaii.PushIndent())
            {
                XivChatType[] chatTypes = Enum.GetValues<XivChatType>()
                    .Where(x => x != XivChatType.StandardEmote)
                    .ToArray();
                string[] chatTypeNames = chatTypes
                    .Select(t => t.GetAttribute<XivChatTypeInfoAttribute>()?.FancyName ?? t.ToString())
                    .ToArray();
                DrawComboOption(_L("Chat channel"), chatTypes, chatTypeNames,
                    () => Configuration.Notifications.ChatType,
                    v => Configuration.Notifications.ChatType = v);

                string[] sounds = ["", .. GetSounds()];
                string[] soundNames = [_L("None"), .. sounds.Skip(1).Select(Path.GetFileNameWithoutExtension).Cast<string>()];
                DrawComboOption(_L("Sound"), sounds, soundNames,
                    () => Configuration.Notifications.Sound,
                    v => Configuration.Notifications.Sound = v);
                ImGui.SameLine();
                using (ImRaii.Disabled(string.IsNullOrEmpty(Configuration.Notifications.Sound)))
                {
                    if (ImGuiComponentsLocal.IconButtonWithText(Dalamud.Interface.FontAwesomeIcon.Play, _L("Test")))
                        NotificationMasterIpc.PlaySound(Configuration.Notifications.Sound);
                }

                bool onlyWhenUnfocused = Configuration.Notifications.OnlyWhenUnfocused;
                if (ImGui.Checkbox(_L("Only when the game is not focused"), ref onlyWhenUnfocused))
                {
                    Configuration.Notifications.OnlyWhenUnfocused = onlyWhenUnfocused;
                    Save();
                }

                ImGui.Separator();
                ImGui.Text(_L("NotificationMaster settings"));
                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("Requires the plugin 'NotificationMaster' to be installed."));
                ImGui.SameLine();
                using (ImRaii.Disabled(!notificationMasterIpc.Enabled))
                {
                    if (ImGuiComponentsLocal.IconButtonWithText(Dalamud.Interface.FontAwesomeIcon.Bell, _L("Test")))
                        notificationMasterIpc.Notify(_L("Test message"));
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
                bool notifyOnStopCondition = Configuration.Notifications.NotifyOnStopCondition;
                if (ImGui.Checkbox(_L("Notify when stop condition is reached"), ref notifyOnStopCondition))
                {
                    Configuration.Notifications.NotifyOnStopCondition = notifyOnStopCondition;
                    Save();
                }
                bool notifyOnCriticalFailure = Configuration.Notifications.NotifyOnCriticalFailure;
                if (ImGui.Checkbox(_L("Notify when Questionable is unable to continue automatic questing"), ref notifyOnCriticalFailure))
                {
                    Configuration.Notifications.NotifyOnCriticalFailure = notifyOnCriticalFailure;
                    Save();
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
