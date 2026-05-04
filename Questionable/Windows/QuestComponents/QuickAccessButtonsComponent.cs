using System;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Questionable.Controller;
namespace Questionable.Windows.QuestComponents;

internal sealed class QuickAccessButtonsComponent
(
    QuestRegistry questRegistry,
    QuestValidationWindow questValidationWindow,
    JournalProgressWindow journalProgressWindow,
    PriorityWindow priorityWindow,
    Configuration configuration,
    ICommandManager commandManager,
    IDalamudPluginInterface pluginInterface)
{
    private readonly ICommandManager _commandManager = commandManager;
    private readonly Configuration _configuration = configuration;
    private readonly JournalProgressWindow _journalProgressWindow = journalProgressWindow;
    private readonly IDalamudPluginInterface _pluginInterface = pluginInterface;
    private readonly PriorityWindow _priorityWindow = priorityWindow;
    private readonly QuestRegistry _questRegistry = questRegistry;
    private readonly QuestValidationWindow _questValidationWindow = questValidationWindow;

    public event EventHandler? Reload;

    public void Draw()
    {
        DrawQuestPriorityButton();
        ImGui.SameLine();
        DrawRebuildNavmeshButton();

        DrawReloadDataButton();
        ImGui.SameLine();
        DrawJournalProgressButton();
        if (!_configuration.General.HideSponsorButton)
        {
            ImGui.SameLine();
            DrawSponsorButton();
        }

        if (_questRegistry.ValidationIssueCount > 0)
        {
            ImGui.SameLine();
            DrawValidationIssuesButton();
        }
    }

    private void DrawQuestPriorityButton()
    {
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Exclamation, "Priority Quests"))
            _priorityWindow.ToggleOrUncollapse();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Configure priority quests which will be done as soon as possible.");
    }

    private void DrawRebuildNavmeshButton()
    {
        bool isNavmeshAvailable = _commandManager.Commands.ContainsKey("/vnav");
        using (ImRaii.Disabled(!isNavmeshAvailable || !ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.GlobeEurope, "Rebuild Navmesh"))
                _commandManager.ProcessCommand("/vnav rebuild");
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (!isNavmeshAvailable)
                ImGui.SetTooltip("vnavmesh is not available.\nPlease install it first.");
            else
                ImGui.SetTooltip("Hold CTRL to enable this button.\nRebuilding the navmesh will take some time.");
        }
    }

    private void DrawReloadDataButton()
    {
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.RedoAlt, "Reload Data"))
            Reload?.Invoke(this, EventArgs.Empty);
    }

    private void DrawJournalProgressButton()
    {
        if (ImGuiComponents.IconButton(FontAwesomeIcon.BookBookmark))
            _journalProgressWindow.IsOpenAndUncollapsed = true;

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Journal Progress");
    }

    private static void DrawSponsorButton()
    {
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Heart, null, null, ImGuiColors.DalamudRed))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/sponsors/alydevs",
                UseShellExecute = true
            });
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Sponsor QST development");
    }

    private void DrawValidationIssuesButton()
    {
        int errorCount = _questRegistry.ValidationErrorCount;
        int infoCount = _questRegistry.ValidationIssueCount - _questRegistry.ValidationErrorCount;
        if (errorCount == 0 && infoCount == 0)
            return;

        int partsToRender = errorCount == 0 || infoCount == 0 ? 1 : 2;
        using ImRaii.IdDisposable id = ImRaii.PushId("validationissues");

        FontAwesomeIcon icon1 = FontAwesomeIcon.ExclamationTriangle;
        FontAwesomeIcon icon2 = FontAwesomeIcon.InfoCircle;
        Vector2 iconSize1, iconSize2;
        using (IDisposable _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            iconSize1 = errorCount > 0 ? ImGui.CalcTextSize(icon1.ToIconString()) : Vector2.Zero;
            iconSize2 = infoCount > 0 ? ImGui.CalcTextSize(icon2.ToIconString()) : Vector2.Zero;
        }

        string text1 = errorCount > 0 ? errorCount.ToString(CultureInfo.InvariantCulture) : string.Empty;
        string text2 = infoCount > 0 ? infoCount.ToString(CultureInfo.InvariantCulture) : string.Empty;
        Vector2 textSize1 = errorCount > 0 ? ImGui.CalcTextSize(text1) : Vector2.Zero;
        Vector2 textSize2 = infoCount > 0 ? ImGui.CalcTextSize(text2) : Vector2.Zero;
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        Vector2 cursor = ImGui.GetCursorScreenPos();

        float iconPadding = 3 * ImGuiHelpers.GlobalScale;

        // Draw an ImGui button with the icon and text
        float buttonWidth = iconSize1.X + iconSize2.X + textSize1.X + textSize2.X +
                            (ImGui.GetStyle().FramePadding.X * 2) + iconPadding * 2 * partsToRender;
        float buttonHeight = ImGui.GetFrameHeight();
        bool button = ImGui.Button(string.Empty, new(buttonWidth, buttonHeight));

        // Draw the icon on the window drawlist
        Vector2 position = new(cursor.X + ImGui.GetStyle().FramePadding.X,
            cursor.Y + ImGui.GetStyle().FramePadding.Y);
        if (errorCount > 0)
        {
            using (IDisposable _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            {
                dl.AddText(position, ImGui.GetColorU32(ImGuiColors.DalamudRed), icon1.ToIconString());
            }

            position = position with { X = position.X + iconSize1.X + iconPadding };

            // Draw the text on the window drawlist
            dl.AddText(position, ImGui.GetColorU32(ImGuiCol.Text), text1);
            position = position with { X = position.X + textSize1.X + 2 * iconPadding };
        }

        if (infoCount > 0)
        {
            using (IDisposable _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            {
                dl.AddText(position, ImGui.GetColorU32(ImGuiColors.ParsedBlue), icon2.ToIconString());
            }

            position = position with { X = position.X + iconSize2.X + iconPadding };

            // Draw the text on the window drawlist
            dl.AddText(position, ImGui.GetColorU32(ImGuiCol.Text), text2);
        }

        if (button)
            _questValidationWindow.ToggleOrUncollapse();
    }
}
