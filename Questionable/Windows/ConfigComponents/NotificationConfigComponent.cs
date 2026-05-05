using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Utility;
namespace Questionable.Windows.ConfigComponents;

internal sealed class NotificationConfigComponent
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration) : ConfigComponent(pluginInterface, configuration)
{

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem("Notifications###Notifications");
        if (!tab)
            return;

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
                ImGui.Text("Desktop notifications");
                ImGuiComponents.HelpMarker("Desktop tray and taskbar notifications are currently unavailable.");
                using (ImRaii.Disabled())
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
