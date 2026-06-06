using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Lumina.Excel.Sheets;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Utils;
using Questionable.Windows.Utils;
namespace Questionable.Windows.JournalComponents;

internal sealed class RedoComponent
(
    RedoUtil redoUtil,
    QuestController questController,
    QuestJournalComponent questJournalComponent,
    QuestData questData,
    QuestRegistry questRegistry)
{
    public void DrawRedoChapters()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem("New Game+");
        if (!tab)
            return;

        using (ImRaii.Disabled(!redoUtil.IsRedoActive()))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Ban, "Stop NG+"))
                RedoUtil.SendRedoCommand(0);
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Quests marked with orange need to be reported as working\n" +
                                   "or not via the LastChecked system. Ask Aly for more details!",
                                   FontAwesomeIcon.InfoCircle, ImGuiColors.DalamudOrange);
        ImGui.SameLine();
        ImGui.Text("Active:");
        ImGui.SameLine();
        redoUtil.TryGetActiveRedoChapter(out var questRedoChapter);
        ImGui.Text(questRedoChapter?.ChapterName.ToString() ?? "None");

        using ImRaii.TableDisposable table = ImRaii.Table("RedoTable", 3, ImGuiTableFlags.NoSavedSettings);
        if (!table)
            return;

        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.NoHide);
        ImGui.TableSetupColumn("Supported", ImGuiTableColumnFlags.WidthFixed, 100 * ImGui.GetIO().FontGlobalScale);
        ImGui.TableSetupColumn("Completed", ImGuiTableColumnFlags.WidthFixed, 100 * ImGui.GetIO().FontGlobalScale);
        ImGui.TableHeadersRow();
        foreach ((QuestRedoChapterUI chapter, RedoCache redoCache) in redoUtil.RedoData)
        {
            if (redoCache.Quests.Count == 0)
                continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var chapterName = redoCache.ChapterUi.ChapterName.ToString() ?? "";
            chapterName = chapterName.Length > 0 ? chapterName : $"???";
            string? categoryName = redoCache.ChapterUi.UITab.Value.Text.ToString();
            categoryName = categoryName != null && categoryName.Length > 0 ? $"{categoryName}: " : "";

            var checkQuests = redoCache.Quests.Select(q => {
                questRegistry.TryGetQuest(new QuestId((ushort)q.RowId), out Model.Quest? quest);
                if (quest != null && (quest.Root.LastChecked.Date == null || 
                        (quest.Root.LastChecked.Date != null &&
                         quest.Root.LastChecked.Since(DateTime.Now)!.Value.TotalDays > 90
                        )))
                    return quest;
                return null;
            }).Where(q => q != null).ToArray();
            ImRaii.ColorDisposable? disposable = null;
            if (checkQuests.Length > 0)
                disposable = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
            bool open = ImGui.TreeNodeEx($"{chapter.RowId}", ImGuiTreeNodeFlags.SpanFullWidth, $"{categoryName}{chapterName}");
            disposable?.Dispose();
            if (checkQuests.Length > 0 && checkQuests[0] != null && ImGui.IsItemHovered())
            {
                using var _ = ImRaii.Tooltip();
                var index = redoUtil.GetChapter(checkQuests[0]!.Id.Value);
                ImGui.Text($"({chapter.RowId}) Unchecked: #{index.SimplifiedIndex}{(checkQuests.Length > 1 ? "+" : "")} ({checkQuests.Length}/{redoCache.Quests.Count})");
                using var __ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
                ImGui.Text(string.Join('\n', checkQuests.Select(q => $"{q?.Info.SimplifiedName} ({q?.Id})")));
            }

            ShowQuestGroupContextMenu($"DrawRedoChapter{chapter.RowId}",
                redoCache.Quests.Select(q =>
                {
                    questData.TryGetQuestInfo(new QuestId((ushort)q.RowId), out IQuestInfo? qInfo);
                    return qInfo;
                }).OfType<IQuestInfo>().ToList(), redoCache, categoryName.StartsWith("???") || chapterName.StartsWith("???"));

            using (ImRaii.PushFont(UiBuilder.MonoFont))
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("-");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("-");
            }
            if (open)
            {
                foreach (var q in redoCache.Quests)
                {
                    if (questRegistry.TryGetQuest(new QuestId((ushort)q.RowId), out Model.Quest? quest))
                        questJournalComponent.DrawQuest(quest.Info);
                    else
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TreeNodeEx($"!Future/Unknown Quest ({(ushort)q.RowId})",
                            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth);
                        using (ImRaii.PushFont(UiBuilder.MonoFont))
                        {
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted("-");
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted("-");
                        }
                    }
                }
                ImGui.TreePop();
            }
        }
    }

    public void ShowQuestGroupContextMenu(string note, List<IQuestInfo> quests, RedoCache redoCache, bool startDisabled)
    {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup($"##QuestGroupPopup{note}");

        using ImRaii.PopupDisposable popup = ImRaii.Popup($"##QuestGroupPopup{note}");
        if (!popup)
            return;

        if (ImGui.MenuItem("Add all to Priority Quests"))
        {
            foreach (IQuestInfo quest in quests)
                questController.PriorityManager.Add(quest.QuestId);
        }

        if (ImGui.MenuItem("Remove all from Priority Quests"))
        {
            foreach (IQuestInfo quest in quests)
                questController.PriorityManager.Remove(quest.QuestId);
        }

        if (ImGui.MenuItem("Sim first quest"))
            if (quests.Count >= 1)
                questController.SimulateQuest(quests[0], 0, 0);
#if DEBUG
        if (!startDisabled)
        {
            using (ImRaii.Disabled(!redoUtil.IsRedoActive()))
            {
                if (ImGui.MenuItem("Start NG+ here") && redoCache.ChapterUi.RowId != 0)
                    RedoUtil.SendRedoCommand(redoCache.ChapterUi);
            }
        }
#endif
    }

}
