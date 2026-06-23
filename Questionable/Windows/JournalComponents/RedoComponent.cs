using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.Throttlers;
using Lumina.Excel.Sheets;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Utils;
using Questionable.Windows.Utils;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable.Windows.JournalComponents;

internal sealed class RedoComponent
(
    RedoUtil redoUtil,
    QuestController questController,
    QuestJournalComponent questJournalComponent,
    QuestData questData,
    QuestRegistry questRegistry,
    QuestFunctions questFunctions,
    Configuration configuration)
{
    private bool _hideDone;
    private bool _expandAll;
    private Dictionary<QuestRedoChapterUI, (int Supported, int Completed, int Total)> _redoCount = [];
    public void DrawRedoChapters()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("New Game+"));
        if (!tab)
            return;

        using (ImRaii.Disabled(EzThrottler.Throttle("stopredo") || !redoUtil.IsRedoActive()))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Ban, ("Stop NG+")))
                redoUtil.SendRedoCommand(redoChapter: RedoChapter.Off);
        }
        if (configuration.Advanced.Debug)
        {
            ImGui.SameLine();
            if (ImGuiComponentsLocal.IconButton(_hideDone ? FontAwesomeIcon.ChevronRight : FontAwesomeIcon.ChevronDown,
                _hideDone ? ImGuiColors.DalamudOrange : null))
            {
                _hideDone = !_hideDone;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Hide chapters that have been completely checked"));
            ImGui.SameLine();
            if (ImGuiComponentsLocal.IconButton(!_expandAll ? FontAwesomeIcon.ChevronRight : FontAwesomeIcon.ChevronDown,
                !_expandAll ? ImGuiColors.DalamudOrange : null))
            {
                _expandAll = !_expandAll;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Expand all categories for this session"));
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(_L("Quests marked with orange need to be reported as working\n" +
                                   "or not via the LastChecked system. Ask Aly for more details!"),
                                   FontAwesomeIcon.InfoCircle, ImGuiColors.DalamudOrange);
        ImGui.SameLine();
        ImGui.Text(_L("Active:"));
        ImGui.SameLine();
        redoUtil.TryGetActiveRedoChapter(out var questRedoChapter);
        ImGui.Text(questRedoChapter?.ChapterName.ToString() ?? ("None"));

        using ImRaii.TableDisposable table = ImRaii.Table("RedoTable", 3, ImGuiTableFlags.NoSavedSettings);
        if (!table)
            return;

        ImGui.TableSetupColumn(_L("Name"), ImGuiTableColumnFlags.NoHide);
        ImGui.TableSetupColumn(_L("Supported"), ImGuiTableColumnFlags.WidthFixed, 100 * ImGui.GetIO().FontGlobalScale);
        ImGui.TableSetupColumn(_L("Completed"), ImGuiTableColumnFlags.WidthFixed, 100 * ImGui.GetIO().FontGlobalScale);
        ImGui.TableHeadersRow();
        foreach ((QuestRedoChapterUI chapter, RedoCache redoCache) in redoUtil.RedoData)
        {
            if (redoCache.Quests.Count == 0)
                continue;
            var chapterName = redoCache.ChapterUi.ChapterName.ToString() ?? "";
            chapterName = chapterName.Length > 0 ? chapterName : _L("???");
            string? categoryName = redoCache.ChapterUi.UITab.Value.Text.ToString();
            categoryName = categoryName != null && categoryName.Length > 0 ? $"{categoryName}: " : "";

            var checkQuests = redoCache.Quests.Select(q =>
            {
                questRegistry.TryGetQuest(new QuestId((ushort)q.RowId), out Model.Quest? quest);
                if (quest != null && (quest.Root.LastChecked.Date == null ||
                        (quest.Root.LastChecked.Date != null &&
                         quest.Root.LastChecked.Since(DateTime.Now)!.Value.TotalDays > 60
                        )))
                    return quest;
                return null;
            }).Where(q => q != null).ToArray();
            if (checkQuests.Length == 0 && _hideDone)
                continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImRaii.ColorDisposable? disposable = null;
            if (checkQuests.Length > 0)
                disposable = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
            ImGui.SetNextItemOpen(_expandAll);
            bool open = ImGui.TreeNodeEx($"{chapter.RowId}", ImGuiTreeNodeFlags.SpanFullWidth, $"{categoryName}{chapterName}");
            disposable?.Dispose();
            if (checkQuests.Length > 0 && checkQuests[0] != null && ImGui.IsItemHovered())
            {
                using var _ = ImRaii.Tooltip();
                var index = redoUtil.GetChapter(checkQuests[0]!.Id.Value);
                ImGui.Text(_LF("({0}) Unchecked: #{1}{2} ({3}/{4})",
                    chapter.RowId, index.SimplifiedIndex, (checkQuests.Length > 1 ? "+" : ""), checkQuests.Length, redoCache.Quests.Count));
                using var __ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
                ImGui.Text(string.Join('\n', checkQuests.Select(q => $"{q?.Info.SimplifiedName} ({q?.Id})")));
            }

            ShowQuestGroupContextMenu($"DrawRedoChapter{chapter.RowId}",
                redoCache.Quests.Select(q =>
                {
                    questData.TryGetQuestInfo(new QuestId((ushort)q.RowId), out IQuestInfo? qInfo);
                    return qInfo;
                }).OfType<IQuestInfo>().ToList(), redoCache, categoryName.StartsWith("???") || chapterName.StartsWith("???"));

            if (_redoCount.TryGetValue(chapter, out (int Supported, int Completed, int Total) result))
            {
                ImGui.TableNextColumn();
                QuestJournalComponent.DrawCount(result.Supported, result.Total);
                ImGui.TableNextColumn();
                QuestJournalComponent.DrawCount(result.Completed, result.Total);
            }
            else
            {
                ImGui.TableNextColumn();
                QuestJournalComponent.DrawCount(0, 0);
                ImGui.TableNextColumn();
                QuestJournalComponent.DrawCount(0, 0);
            }
            if (open)
            {
                int _supported = 0;
                int _completed = 0;
                int _total = 0;
                foreach (var q in redoCache.Quests)
                {
                    if (questRegistry.TryGetQuest(new QuestId((ushort)q.RowId), out Model.Quest? quest))
                    {
                        _supported += 1;
                        questJournalComponent.DrawQuest(quest.Info);
                        if (questFunctions.IsQuestComplete(quest.Id))
                            _completed += 1;
                    }
                    else
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TreeNodeEx(_LF("{0} ({1})", q.Name, (ushort)q.RowId),
                            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth);

                        ImGui.TableNextColumn();
                        QuestJournalComponent.DrawCount(0, 0);
                        ImGui.TableNextColumn();
                        QuestJournalComponent.DrawCount(0, 0);
                    }
                    _total += 1;
                }
                if (!_redoCount.TryGetValue(chapter, out var _result) || _result.Supported != _supported || _result.Completed != _completed)
                    _redoCount[chapter] = (_supported, _completed, _total);
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

        if (ImGui.MenuItem(_L("Add all to Priority Quests")))
        {
            foreach (IQuestInfo quest in quests)
                questController.PriorityManager.Add(quest.QuestId);
        }

        if (ImGui.MenuItem(_L("Remove all from Priority Quests")))
        {
            foreach (IQuestInfo quest in quests)
                questController.PriorityManager.Remove(quest.QuestId);
        }

        if (ImGui.MenuItem(_L("Sim first quest")))
            if (quests.Count >= 1)
                questController.SimulateQuest(quests[0], 0, 0);

        if (configuration.Advanced.Debug && !startDisabled)
        {
            bool redoActive = redoUtil.IsRedoActive();
            using (ImRaii.Disabled(redoActive))
            {
                if (ImGui.MenuItem(_L("Start NG+ here")) && redoCache.ChapterUi.RowId != 0)
                {
                    if (redoActive) // safeguard
                        redoUtil.SendRedoCommand(redoChapter: RedoChapter.Off);
                    else
                        redoUtil.SendRedoCommand(questRedoChapter: redoCache.ChapterUi);
                }
            }
            if (redoActive && ImGui.MenuItem(_L("Stop NG+")))
                redoUtil.SendRedoCommand(redoChapter: RedoChapter.Off);
        }

    }

}
