using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LLib.ImGui;
using Questionable.Controller;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Windows.QuestComponents;
using Questionable.Windows.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Questionable.Windows;

internal sealed class PriorityWindow : LWindow
{
    private const string ClipboardPrefix = "qst:v1:";
    private const char ClipboardSeparator = ';';

    private readonly QuestController _questController;
    private readonly QuestFunctions _questFunctions;
    private readonly QuestSelector _questSelector;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly UiUtils _uiUtils;
    private readonly IChatGui _chatGui;
    private readonly IDalamudPluginInterface _pluginInterface;

    private ElementId? _draggedItem;

    public PriorityWindow(QuestController questController, QuestFunctions questFunctions, QuestSelector questSelector,
        QuestTooltipComponent questTooltipComponent, UiUtils uiUtils, IChatGui chatGui,
        IDalamudPluginInterface pluginInterface)
        : base("Quest Priority###QuestionableQuestPriority")
    {
        _questController = questController;
        _questFunctions = questFunctions;
        _questSelector = questSelector;
        _questTooltipComponent = questTooltipComponent;
        _uiUtils = uiUtils;
        _chatGui = chatGui;
        _pluginInterface = pluginInterface;

        _questSelector.SuggestionPredicate = quest =>
            !quest.Info.IsMainScenarioQuest &&
            !questFunctions.IsQuestUnobtainable(quest.Id) &&
            questController.ManualPriorityQuests.All(x => x.Id != quest.Id);
        _questSelector.DefaultPredicate = quest => questFunctions.IsQuestAccepted(quest.Id);
        _questSelector.QuestSelected = quest => _questController.ManualPriorityQuests.Add(quest);

        Size = new Vector2(400, 400);
        SizeCondition = ImGuiCond.Once;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 400),
            MaximumSize = new Vector2(400, 999)
        };
    }

    public override void DrawContent()
    {
        ImGui.Text("Quests to do first:");
        _questSelector.DrawSelection();
        DrawQuestList();

        List<ElementId> clipboardItems = ParseClipboardItems();
        ImGui.BeginDisabled(clipboardItems.Count == 0);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Download, "Import from Clipboard"))
            ImportFromClipboard(clipboardItems);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(_questController.ManualPriorityQuests.Count == 0);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Upload, "Export to Clibpoard"))
            ExportToClipboard();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Check, "Remove finished Quests"))
            _questController.ManualPriorityQuests.RemoveAll(q => _questFunctions.IsQuestComplete(q.Id));
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, "Clear"))
            _questController.ClearQuestPriority();
        ImGui.EndDisabled();

        ImGui.Spacing();

        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped(
            "If you have an active MSQ quest, Questionable will generally try to do:");
        ImGui.BulletText("'Priority' quests: class quests, ARR primals, ARR raids");
        ImGui.BulletText(
            "Supported quests in your 'To-Do list'\n(quests from your Journal that are always on-screen)");
        ImGui.BulletText("MSQ quest (if available, unless it is marked as 'ignored'\nin your Journal)");
        ImGui.TextWrapped(
            "If you don't have any active MSQ quest, it will always try to pick up the next quest in the MSQ first.");
    }

    private void DrawQuestList()
    {
        List<Quest> priorityQuests = _questController.ManualPriorityQuests;
        Quest? itemToRemove = null;
        Quest? itemToAdd = null;
        int indexToAdd = 0;

        float width = ImGui.GetContentRegionAvail().X;
        List<(Vector2 TopLeft, Vector2 BottomRight)> itemPositions = [];

        for (int i = 0; i < priorityQuests.Count; ++i)
        {
            Vector2 topLeft = ImGui.GetCursorScreenPos() +
                              new Vector2(0, -ImGui.GetStyle().ItemSpacing.Y / 2);
            var quest = priorityQuests[i];
            using (ImRaii.PushId($"Quest{quest.Id}"))
            {
                var style = _uiUtils.GetQuestStyle(quest.Id);
                bool hovered;
                using (var _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored(style.Color, style.Icon.ToIconString());
                    hovered = ImGui.IsItemHovered();
                }

                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(quest.Info.Name);
                hovered |= ImGui.IsItemHovered();

                if (hovered)
                    _questTooltipComponent.Draw(quest.Info);

                if (priorityQuests.Count > 1)
                {
                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X +
                                       ImGui.GetStyle().WindowPadding.X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.ArrowsUpDown.ToIconString()).X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X -
                                       ImGui.GetStyle().FramePadding.X * 4 -
                                       ImGui.GetStyle().ItemSpacing.X);
                    }

                    if (_draggedItem == quest.Id)
                    {
                        ImGuiComponents.IconButton("##Move", FontAwesomeIcon.ArrowsUpDown,
                            ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(ImGuiCol.ButtonActive)));
                    }
                    else
                        ImGuiComponents.IconButton("##Move", FontAwesomeIcon.ArrowsUpDown);

                    if (_draggedItem == null && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                        _draggedItem = quest.Id;

                    ImGui.SameLine();
                }
                else
                {
                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X +
                                       ImGui.GetStyle().WindowPadding.X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X -
                                       ImGui.GetStyle().FramePadding.X * 2);
                    }
                }

                if (ImGuiComponents.IconButton($"##Remove{i}", FontAwesomeIcon.Times))
                    itemToRemove = quest;
            }

            Vector2 bottomRight = new Vector2(topLeft.X + width,
                ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y + 2);
            itemPositions.Add((topLeft, bottomRight));
        }

        if (!ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            _draggedItem = null;
        else if (_draggedItem != null)
        {
            var draggedItem = priorityQuests.Single(x => x.Id == _draggedItem);
            int oldIndex = priorityQuests.IndexOf(draggedItem);

            var (topLeft, bottomRight) = itemPositions[oldIndex];
            ImGui.GetWindowDrawList().AddRect(topLeft, bottomRight, ImGui.GetColorU32(ImGuiColors.DalamudGrey), 3f,
                ImDrawFlags.RoundCornersAll);

            int newIndex = itemPositions.FindIndex(x => ImGui.IsMouseHoveringRect(x.TopLeft, x.BottomRight, true));
            if (newIndex >= 0 && oldIndex != newIndex)
            {
                itemToAdd = priorityQuests.Single(x => x.Id == _draggedItem);
                indexToAdd = newIndex;
            }
        }

        if (itemToRemove != null)
        {
            priorityQuests.Remove(itemToRemove);
        }

        if (itemToAdd != null)
        {
            priorityQuests.Remove(itemToAdd);
            priorityQuests.Insert(indexToAdd, itemToAdd);
        }
    }

    private static List<ElementId> ParseClipboardItems()
    {
        string clipboardText = ImGui.GetClipboardText().Trim();
        return DecodeQuestPriority(clipboardText);
    }

    public static List<ElementId> DecodeQuestPriority(string clipboardText)
    {
        List<ElementId> clipboardItems = new List<ElementId>();
        try
        {
            if (!string.IsNullOrEmpty(clipboardText) && clipboardText.StartsWith(ClipboardPrefix, StringComparison.InvariantCulture))
            {
                clipboardText = clipboardText.Substring(ClipboardPrefix.Length);
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(clipboardText));
                foreach (string part in text.Split(ClipboardSeparator))
                {
                    ElementId elementId = ElementId.FromString(part);
                    clipboardItems.Add(elementId);
                }
            }
        }
        catch (Exception)
        {
            clipboardItems.Clear();
        }

        return clipboardItems;
    }

    public string EncodeQuestPriority()
    {
        return ClipboardPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(
            string.Join(ClipboardSeparator, _questController.ManualPriorityQuests.Select(x => x.Id.ToString()))));
    }

    private void ExportToClipboard()
    {
        string clipboardText = EncodeQuestPriority();
        ImGui.SetClipboardText(clipboardText);
        _chatGui.Print("Copied quests to clipboard.", CommandHandler.MessageTag, CommandHandler.TagColor);
    }

    private void ImportFromClipboard(List<ElementId> questElements)
    {
        _questController.ImportQuestPriority(questElements);
    }
}
