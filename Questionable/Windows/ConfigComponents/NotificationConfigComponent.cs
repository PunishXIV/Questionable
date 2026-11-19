using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
namespace Questionable.Windows.ConfigComponents;

[RegisterSingleton]
internal sealed class NotificationConfigComponent
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    NotificationMasterIpc notificationMasterIpc) : ConfigComponent(pluginInterface, configuration)
{

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Notifications") + "###Notifications");
        if (!tab)
            return;

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
                XivChatType[] xivChatTypes = Enum.GetValues<XivChatType>()
                    .Where(x => x != XivChatType.StandardEmote)
                    .ToArray();
                string[] chatTypeNames = xivChatTypes
                    .Select(t => t.GetAttribute<XivChatTypeInfoAttribute>()?.FancyName ?? t.ToString())
                    .ToArray();
                DrawComboOption(_L("Chat channel"), xivChatTypes, chatTypeNames,
                    () => Configuration.Notifications.ChatType,
                    v => Configuration.Notifications.ChatType = v);

                ImGui.Separator();
                ImGui.Text(_L("NotificationMaster settings"));
                ImGui.SameLine();
                ImGuiComponents.HelpMarker(_L("Requires the plugin 'NotificationMaster' to be installed."));
                ImGui.SameLine();
                using (ImRaii.Disabled(!notificationMasterIpc.Enabled))
                {
                    if (ImGuiComponentsLocal.IconButtonWithText(Dalamud.Interface.FontAwesomeIcon.NotesMedical, _L("Test")))
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
                if (ImGui.Checkbox(_L("Notify when QST is unable to continue automatic questing"), ref notifyOnCriticalFailure))
                {
                    Configuration.Notifications.NotifyOnCriticalFailure = notifyOnCriticalFailure;
                    Save();
                }
            }
        }
    }
}
