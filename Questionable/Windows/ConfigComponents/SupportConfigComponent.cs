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
    private const string GithubUrl = "https://github.com/PunishXIV/Questionable";
    private const string WikiUrl = "https://github.com/PunishXIV/Questionable/wiki";
    private const string AkechiSponsorUrl = "https://ko-fi.com/akechikun";
    private const string LimianaSponsorUrl = "https://www.patreon.com/NightmareXIV";

    private static readonly string[] HeaderTextLines =
    {
        " - Originated by Liza Carvelli - ",
        "- Maintained by Akechi and Limiana - ",
        " Contributions are always welcome and greatly appreciated! ",
        " We appreciate any contributions in the continued development of this plugin. ",
        " Please refer to the buttons below for more information. Thank you for your support! "
    };

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

    private static void DrawHeaderText(float contentWidth)
    {
        foreach (var line in HeaderTextLines)
        {
            var textSize = ImGui.CalcTextSize(line);
            var centerOffset = (contentWidth - textSize.X) * 0.5f;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centerOffset);
            ImGui.TextUnformatted(line);
            if (Array.IndexOf(HeaderTextLines, line) < HeaderTextLines.Length - 1)
            {
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4f);
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

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(
                        " Join our beautiful Discord community to stay up-to-date with the \n" +
                        " latest updates and share feedback directly with the developers. ");
                    ImGui.EndTooltip();
                }
            }
            ImGui.SameLine();

            //Github
            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.04f, 0.17f, 0.35f, 1.0f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.06f, 0.24f, 0.49f, 1.0f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.03f, 0.12f, 0.25f, 1.0f)))
            {
                if (ImGui.Button("Github", new Vector2(ButtonWidth, ButtonHeight)))
                    OpenLink(GithubUrl);

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    OpenLink(WikiUrl);

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Access our repository for the source code, or consult our Wiki for FAQs, setup, and other documentation.");
                    ImGui.TextUnformatted("Left Click -> Repository");
                    ImGui.TextUnformatted("Right Click -> Wiki");
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
                    ImGui.Text("The project is currently maintained by two developers.");
                    ImGui.TextUnformatted("Left Click -> Sponsor ");
                    ImGui.SameLine(0, 0);
                    using (ImRaii.PushColor(ImGuiCol.Text, 0xFFE1D18E))
                        ImGui.Text("Akechi");
                    ImGui.TextUnformatted("Right Click -> Sponsor ");
                    ImGui.SameLine(0, 0);
                    using (ImRaii.PushColor(ImGuiCol.Text, 0xCCD86EB8))
                        ImGui.Text("Limiana");
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