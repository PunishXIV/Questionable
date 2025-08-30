using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Numerics;

namespace Questionable.Windows.ConfigComponents;

internal sealed class SupportConfigComponent : ConfigComponent
{
    private readonly ILogger<SupportConfigComponent> _logger;

    private const float ButtonWidth = 180f;
    private const float ButtonHeight = 40f;
    private const string DiscordUrl = "https://discord.gg/Zzrcc8kmvy";
    private const string DiscordChannelUrl = "https://discord.com/channels/1001823907193552978/1408201462722596945";
    private const string GitHubUrl = "https://GitHub.com/PunishXIV/Questionable";
    private const string WikiUrl = "https://GitHub.com/PunishXIV/Questionable/wiki";
    private const string AkechiSponsorUrl = "https://ko-fi.com/akechikun";
    private const string LimianaSponsorUrl = "https://www.patreon.com/NightmareXIV";

    public SupportConfigComponent(IDalamudPluginInterface pluginInterface, Configuration configuration, ILogger<SupportConfigComponent> logger) 
        : base(pluginInterface, configuration) => _logger = logger;

    public override void DrawTab()
    {
        using var tab = ImRaii.TabItem("Support###Support");
        if (!tab)
            return;

        var windowWidth = ImGui.GetWindowWidth();
        var contentWidth = windowWidth - (ImGui.GetStyle().WindowPadding.X * 2);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5f);
        DrawHeaderText(contentWidth);
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 12f);
        DrawSupportButtons(contentWidth);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5f);
    }

    private static readonly string[] HeaderTextLines =
    {
        " - Originated by Liza Carvelli - ",
        " - Maintained by Akechi and Limiana - ",
        " Contributions are always welcome! ",
        " We appreciate any support given in the continued development of this plugin. ",
        " Please refer the buttons below for more information. ",
        " Thank you! "
    };

    private static void DrawHeaderText(float contentWidth)
    {
        for (int i = 0; i < HeaderTextLines.Length; i++)
        {
            var line = HeaderTextLines[i];
            var textSize = ImGui.CalcTextSize(line);
            var centerOffset = (contentWidth - textSize.X) * 0.5f;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centerOffset);

            if (line.Contains("Liza Carvelli", StringComparison.Ordinal))
            {
                using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1.0f, 0.9f, 0.4f, 1.0f)))
                    ImGui.TextUnformatted(line);
            }
            else if (line.Contains("Akechi", StringComparison.Ordinal) || line.Contains("Limiana", StringComparison.Ordinal))
            {
                using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.7f, 0.8f, 1.0f, 1.0f)))
                    ImGui.TextUnformatted(line);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }
            else
            {
                ImGui.TextUnformatted(line);
            }
        }
    }

    private void DrawSupportButtons(float contentWidth)
    {
        var buttonCount = 3;
        var totalButtonWidth = (buttonCount * ButtonWidth) + ((buttonCount - 1) * ImGui.GetStyle().ItemSpacing.X);
        var buttonsStartX = (contentWidth - totalButtonWidth) * 0.5f;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + buttonsStartX);

        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(ImGui.GetStyle().FramePadding.X, 3f)))
        {
            //Discord
            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.29f, 0.30f, 0.62f, 1.0f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.36f, 0.71f, 1.0f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.24f, 0.25f, 0.53f, 1.0f)))
            {
                if (ImGui.Button("Discord", new Vector2(ButtonWidth, ButtonHeight)))
                    OpenLink(DiscordUrl);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    OpenLink(DiscordChannelUrl);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.2f, 1.0f)))
                        ImGui.Text("Left Click");
                    ImGui.SameLine();
                    ImGui.Text("→");
                    ImGui.SameLine();
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 1.0f, 1.0f)))
                        ImGui.Text("Puni.sh Discord Server Invite");
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1.0f)))
                        ImGui.TextWrapped("Join our Discord community to stay up-to-date with the latest updates and announcements.");
                    ImGui.Spacing();
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.2f, 1.0f)))
                        ImGui.Text("Right Click");
                    ImGui.SameLine();
                    ImGui.Text("→");
                    ImGui.SameLine();
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 1.0f, 1.0f)))
                        ImGui.Text("Questionable's Dedicated Channel");
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1.0f)))
                        ImGui.TextWrapped("Share feedback directly with the developers of the plugin by visiting our Discord channel.");
                    ImGui.EndTooltip();
                }
            }
            ImGui.SameLine();

            //GitHub
            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.04f, 0.17f, 0.35f, 1.0f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.06f, 0.24f, 0.49f, 1.0f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.03f, 0.12f, 0.25f, 1.0f)))
            {
                if (ImGui.Button("GitHub", new Vector2(ButtonWidth, ButtonHeight)))
                    OpenLink(GitHubUrl);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    OpenLink(WikiUrl);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.2f, 1.0f)))
                        ImGui.Text("Left Click");
                    ImGui.SameLine();
                    ImGui.Text("→");
                    ImGui.SameLine();
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 1.0f, 1.0f)))
                        ImGui.Text("GitHub Repository");
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1.0f)))
                        ImGui.TextWrapped("Access our repository to view source code, report issues, make pull requests, and more.");
                    ImGui.Spacing();
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.2f, 1.0f)))
                        ImGui.Text("Right Click");
                    ImGui.SameLine();
                    ImGui.Text("→");
                    ImGui.SameLine();
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 1.0f, 1.0f)))
                        ImGui.Text("GitHub Wiki Homepage");
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1.0f)))
                        ImGui.TextWrapped("Consult our Wiki page for FAQs, guides, and other useful documentation on the plugin.");
                    ImGui.EndTooltip();
                }
            }
            ImGui.SameLine();

            //Sponsor
            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.75f, 0.29f, 0.08f, 1.0f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.85f, 0.35f, 0.12f, 1.0f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.65f, 0.23f, 0.05f, 1.0f)))
            {
                if (ImGui.Button("Sponsor", new Vector2(ButtonWidth, ButtonHeight)))
                    OpenLink(AkechiSponsorUrl);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    OpenLink(LimianaSponsorUrl);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.2f, 1.0f)))
                        ImGui.Text("Left Click");
                    ImGui.SameLine();
                    ImGui.Text("→");
                    ImGui.SameLine();
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 1.0f, 1.0f)))
                        ImGui.Text("Sponsor Akechi");
                    ImGui.Spacing();
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.2f, 1.0f)))
                        ImGui.Text("Right Click");
                    ImGui.SameLine();
                    ImGui.Text("→");
                    ImGui.SameLine();
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 1.0f, 1.0f)))
                        ImGui.Text("Sponsor Limiana");
                    ImGui.EndTooltip();
                }
            }
        }
    }
    private void OpenLink(string link)
    {
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            //TODO: fix later, I'm tired
#pragma warning disable CA1848 //Use delegates
#pragma warning disable CA2254 //Template should be a static expression
            _logger.LogError($"Failed to open link: {ex.Message}");
#pragma warning restore CA1848 //Use delegates
#pragma warning restore CA2254 //Template should be a static expression
        }
    }
}