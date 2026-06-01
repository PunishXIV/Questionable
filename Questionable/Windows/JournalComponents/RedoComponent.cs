using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Lumina.Excel.Sheets;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Windows.QuestComponents;
using Questionable.Windows.Utils;
namespace Questionable.Windows.JournalComponents;

internal sealed class RedoComponent
(
    RedoUtil redoUtil,
    QuestJournalComponent questJournalComponent,
    QuestJournalUtils questJournalUtils,
    QuestData questData,
    QuestRegistry questRegistry)
{
    public void DrawRedoChapters()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem("New Game+");
        if (!tab)
            return;

        using ImRaii.TableDisposable table = ImRaii.Table("RedoTable", 3, ImGuiTableFlags.NoSavedSettings);
        if (!table)
            return;

        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.NoHide);
        ImGui.TableSetupColumn("Supported", ImGuiTableColumnFlags.WidthFixed, 100 * ImGui.GetIO().FontGlobalScale);
        ImGui.TableSetupColumn("Completed", ImGuiTableColumnFlags.WidthFixed, 100 * ImGui.GetIO().FontGlobalScale);
        ImGui.TableHeadersRow();
        foreach ((QuestRedoChapter chapter, RedoCache redoCache) in redoUtil.RedoData)
        {
            if (redoCache.Quests.Count == 0)
                continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var chapterName = redoCache.ChapterUi?.ChapterName.ToString() ?? "";
            chapterName = chapterName.Length > 0 ? chapterName : $"???";
            string? categoryName = redoCache.ChapterUi?.UITab.Value.Text.ToString();
            categoryName = categoryName != null && categoryName.Length > 0 ? $"{categoryName}: " : "";
            bool open = ImGui.TreeNodeEx($"{chapter.RowId}", ImGuiTreeNodeFlags.SpanFullWidth, $"{categoryName}{chapterName}");

            questJournalUtils.ShowQuestGroupContextMenu($"DrawRedoChapter{chapter.RowId}",
                redoCache.Quests.Select(q =>
                {
                    questData.TryGetQuestInfo(new QuestId((ushort)q.RowId), out IQuestInfo? qInfo);
                    return qInfo;
                }).OfType<IQuestInfo>().ToList());

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
                        ImGui.TreeNodeEx($"!Future Quest ({(ushort)q.RowId})",
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

}
