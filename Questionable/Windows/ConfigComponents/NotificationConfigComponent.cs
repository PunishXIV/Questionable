using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Utility;
using Questionable.External;
using System;
using System.Linq;
namespace Questionable.Windows.ConfigComponents;

internal sealed class NotificationConfigComponent
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    NotificationMasterIpc notificationMasterIpc) : ConfigComponent(pluginInterface, configuration)
{
    private readonly NotificationMasterIpc _notificationMasterIpc = notificationMasterIpc;

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem("Notifications###Notifications");
        if (!tab)
        {
            return;
        }

        bool enabled = Configuration.Notifications.Enabled;
        if (ImGui.Checkbox("Enable notifications when manual interaction is required", ref enabled))
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
                int selectedChatType = Array.IndexOf(xivChatTypes, Configuration.Notifications.ChatType);
                string[] chatTypeNames = xivChatTypes
                    .Select(t => t.GetAttribute<XivChatTypeInfoAttribute>()?.FancyName ?? t.ToString())
                    .ToArray();
                if (ImGui.Combo("Chat channel", ref selectedChatType, chatTypeNames,
                    chatTypeNames.Length))
                {
                    Configuration.Notifications.ChatType = xivChatTypes[selectedChatType];
                    Save();
                }

                ImGui.Separator();
                ImGui.Text("NotificationMaster settings");
                ImGui.SameLine();
                ImGuiComponents.HelpMarker("Requires the plugin 'NotificationMaster' to be installed.");
                using (ImRaii.Disabled(!_notificationMasterIpc.Enabled))
                {
                    bool showTrayMessage = Configuration.Notifications.ShowTrayMessage;
                    if (ImGui.Checkbox("Show tray notification", ref showTrayMessage))
                    {
                        Configuration.Notifications.ShowTrayMessage = showTrayMessage;
                        Save();
                    }

                    bool flashTaskbar = Configuration.Notifications.FlashTaskbar;
                    if (ImGui.Checkbox("Flash taskbar icon", ref flashTaskbar))
                    {
                        Configuration.Notifications.FlashTaskbar = flashTaskbar;
                        Save();
                    }
                }
            }
        }
    }
}
