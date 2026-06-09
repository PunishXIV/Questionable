using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using Questionable.Controller;
using Questionable.Controller.Steps.Shared;
using Questionable.Functions;
using Questionable.Utils;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable.Windows.QuestComponents;

internal sealed class QuickAccessButtonsComponent
(
    QuestController questController,
    QuestRegistry questRegistry,
    QuestValidationWindow questValidationWindow,
    JournalProgressWindow journalProgressWindow,
    PriorityWindow priorityWindow,
    ICommandManager commandManager,
    IDalamudPluginInterface pluginInterface)
{

    public event EventHandler? Reload;

    public void Draw()
    {
        DrawPriorityQuestsButton();
        ImGui.SameLine();
        DrawJournalProgressButton();

        DrawReloadDataButton();
        ImGui.SameLine();
        DrawRebuildNavmeshButton();

        DrawTroubleshootingButton(questController.CurrentQuest);

        if (questRegistry.ValidationIssueCount > 0)
        {
            ImGui.SameLine();
            DrawValidationIssuesButton();
        }
    }

    private void DrawPriorityQuestsButton()
    {
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.ExclamationCircle, _L("Priority Quests")))
            priorityWindow.ToggleOrUncollapse();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Configure priority quests which will be done as soon as possible."));
    }

    private void DrawRebuildNavmeshButton()
    {
        bool isNavmeshAvailable = commandManager.Commands.ContainsKey("/vnav");
        using (ImRaii.Disabled(!isNavmeshAvailable || !ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.GlobeEurope, _L("Rebuild Navmesh")))
                commandManager.ProcessCommand("/vnav rebuild");
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (!isNavmeshAvailable)
                ImGui.SetTooltip(_L("vnavmesh is not available.\nPlease install it first."));
            else
                ImGui.SetTooltip(_L("Hold CTRL to enable this button.\nRebuilding the navmesh will take some time."));
        }
    }

    private void DrawReloadDataButton()
    {
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.RedoAlt, _L("Reload Data")))
            Reload?.Invoke(this, EventArgs.Empty);
    }

    private void DrawJournalProgressButton()
    {
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.BookBookmark, _L("Journal Progress")))
            journalProgressWindow.ToggleOrUncollapse();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Journal Progress"));
    }

    private static void DrawTroubleshootingButton(QuestController.QuestProgress? questProgress)
    {
        bool leftClicked = ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Handshake, _L("Stuck?"));
        bool rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Left click: Copy troubleshooting information to clipboard\nRight click: Copy list of completed quests to clipboard"));
        if (leftClicked || rightClicked)
        {
            string output = "";
            List<LogQuestCompletion.QuestCompletion> questCompletions = LogQuestCompletion.ReadQuestCompletions();
            if (rightClicked)
            {
                output = JsonSerializer.Serialize(questCompletions, JsonOptions.Default);
                ImGui.SetClipboardText(output);
                Svc.Chat.Print(_L("List of completed quests has been copied to clipboard. Please paste it to this discord channel, and then run " +
                        "'/qst clearlog' to reset the log.") + "\nhttps://discord.com/channels/1001823907193552978/1447612869431656508/1447612869431656508",
                        CommandHandler.MessageTag, CommandHandler.TagColor);
            }
            else
            {
                // Dalamud troubleshooting json is written after plugin manager changes; we can't access the data from dalamud directly
                SortedDictionary<string, string>? plugins = [];
                try
                {
                    JsonNode? dalTrouble = JsonNode.Parse(
                            File.ReadAllText(Path.Join(Svc.PluginInterface.DalamudAssetDirectory.Parent?.Parent?.FullName, "dalamud.troubleshooting.json"))
                        );
                    var pluginNames = dalTrouble?["PluginStates"]
                        .Deserialize<SortedDictionary<string, string>>()
                        ?.Where(kvp => kvp.Value == "Loaded")
                        .Select(kvp => kvp.Key)
                        .ToHashSet();
                    plugins = new(dalTrouble?["LoadedPlugins"]
                        ?.AsArray()
                        .Where(node =>
                            node?["InstalledFromUrl"]?.GetValue<string>() is { Length: > 0 } &&
                            node?["InternalName"]?.GetValue<string>() is { } name &&
                            pluginNames?.Contains(name) == true)
                        .ToDictionary(
                            node => node!["Name"]!.GetValue<string>(),
                            node => node!["AssemblyVersion"]!.GetValue<string>() ?? "unknown"
                        ) ?? []);
                }
                catch (Exception) { }
                Dictionary<string, object?> troubleshooting = new(){
                    { "LoadedPlugins", plugins },
                    { "QST", new Dictionary<string,string>(){
                        { "Version", CommandHandler.MessageTag },
                        { "Debug", 
                        #if DEBUG
                        "true"
                        #else
                        "false"
                        #endif
                        }
                    } },
                    { "Configuration", Svc.PluginInterface.GetPluginConfig() },
                    { "CompletedQuests", questCompletions.Count },
                    { "QuestProgress", new Dictionary<string,object?>(){
                        { "ToString", questProgress?.ToString() },
                        { "QW", questProgress != null ? QuestFunctions.GetQuestProgressInfo(questProgress.Quest.Id) : "Error: questProgress is null" }
                    }},
                };
                output = JsonSerializer.Serialize(troubleshooting, JsonOptions.Default);
                ImGui.SetClipboardText(output);
                Svc.Chat.Print(_L("Troubleshooting information has been copied to clipboard. " +
                    "Please create a new thread in #questionable-issues in https://discord.gg/punishxiv describing the problem and pasting this troubleshooting information."),
                    CommandHandler.MessageTag, CommandHandler.TagColor);
            }
        }
    }

    private void DrawValidationIssuesButton()
    {
        int errorCount = questRegistry.ValidationErrorCount;
        int infoCount = questRegistry.ValidationIssueCount - questRegistry.ValidationErrorCount;
        if (errorCount == 0 && infoCount == 0)
            return;

        int partsToRender = errorCount == 0 || infoCount == 0 ? 1 : 2;
        using ImRaii.IdDisposable id = ImRaii.PushId("validationissues");

        FontAwesomeIcon icon1 = FontAwesomeIcon.ExclamationTriangle;
        FontAwesomeIcon icon2 = FontAwesomeIcon.InfoCircle;
        Vector2 iconSize1, iconSize2;
        using (IDisposable _ = pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
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
            using (IDisposable _ = pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
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
            using (IDisposable _ = pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            {
                dl.AddText(position, ImGui.GetColorU32(ImGuiColors.ParsedBlue), icon2.ToIconString());
            }

            position = position with { X = position.X + iconSize2.X + iconPadding };

            // Draw the text on the window drawlist
            dl.AddText(position, ImGui.GetColorU32(ImGuiCol.Text), text2);
        }

        if (button)
            questValidationWindow.ToggleOrUncollapse();
    }
}
