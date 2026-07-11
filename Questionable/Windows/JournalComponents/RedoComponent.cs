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
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Utils;
using Questionable.Windows.QuestComponents;
using Questionable.Windows.Utils;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable.Windows.JournalComponents;

internal sealed class RedoComponent
(
    RedoUtil redoUtil,
    QuestController questController,
    QuestJournalComponent questJournalComponent,
    QuestTooltipComponent questTooltipComponent,
    QuestData questData,
    QuestRegistry questRegistry,
    QuestFunctions questFunctions,
    Configuration configuration,
    UiUtils uiUtils)
{
    private bool _hideDone;
    private readonly Dictionary<QuestRedoChapterUI, (int Supported, int Completed, int Total)> _redoCount = [];
    private Model.Quest? _unlockQuest;
    public void DrawRedoChapters()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("New Game+"));
        if (!tab)
            return;

        // Disable tab if ng+ unlock quest is incomplete
        bool disabled = false;
        if ((_unlockQuest != null || questRegistry.TryGetQuest(new QuestId(3759), out _unlockQuest)) &&
            (!questFunctions.IsQuestComplete(_unlockQuest.Id) || disabled))
        {
            disabled = true;
            (bool locked, var _) = questFunctions.IsQuestLocked(_unlockQuest.Id);
            using var _ = ImRaii.Disabled(locked);
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Play, _L("Unlock NG+")))
            {
                questController.SetNextQuest(_unlockQuest);
                questController.StartSingleQuest("Unlock NG+");
            }
            if (ImGui.IsItemHovered())
                questTooltipComponent.Draw(_unlockQuest.Info);
        }

        if (disabled)
        {
            ImGui.Text(_L("New Game+ has not been unlocked on this character"));
            return;
        }
        using (ImRaii.Disabled(!redoUtil.IsRedoActive()))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Ban, _L("Stop NG+")))
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
            var _preTotal = redoCache.Quests.Count;
            if (_preTotal == 0)
                continue;
            var chapterName = redoCache.ChapterUi.ChapterName.ToString() ?? "";
            chapterName = chapterName.Length > 0 ? chapterName : _L("???");
            string? categoryName = redoCache.ChapterUi.UITab.Value.Text.ToString();
            categoryName = categoryName != null && categoryName.Length > 0 ? $"{categoryName}: " : "";

            bool showAnyway = false;
            var checkQuests = redoCache.Quests.Select(q =>
            {
                var qid = new QuestId((ushort)q.RowId);
                bool unobtainable = questData.TryGetQuestInfo(qid, out var qInfo) && questFunctions.IsQuestUnobtainable(qid);
                questRegistry.TryGetQuest(qid, out Model.Quest? quest);
                if (qInfo != null && quest != null && (quest.Root.LastChecked.Date == null ||
                        (quest.Root.LastChecked.Date != null &&
                         quest.Root.LastChecked.Since(DateTime.Now)!.Value.TotalDays > 60)) &&
                        !unobtainable)
                    return quest;
                if (!unobtainable && qInfo != null && quest == null)
                    showAnyway = true;
                return null;
            }).Where(q => q != null).ToArray();
            if (checkQuests.Length == 0 && _hideDone && !showAnyway)
                continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImRaii.ColorDisposable? disposable = null;
            if (checkQuests.Length > 0)
                disposable = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
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
                ImGui.TableNextColumn();
                QuestJournalComponent.DrawCount(0, _preTotal);
            }
            if (open)
            {
                int _supported = 0;
                int _completed = 0;
                int _total = 0;
                foreach (var q in redoCache.Quests)
                {
                    var qid = new QuestId((ushort)q.RowId);
                    questData.TryGetQuestInfo(qid, out IQuestInfo? qInfo);
                    if (qInfo != null)
                        questJournalComponent.DrawQuest(qInfo);
                    else
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TreeNodeEx($"{q.Name} ({(ushort)q.RowId})",
                            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth);
                        ImGui.TableNextColumn();
                        if (uiUtils.ChecklistItem("", ImGuiColors.DalamudGrey, FontAwesomeIcon.Minus))
                            ImGui.SetTooltip(_L("This quest is not supported."));
                        ImGui.TableNextColumn();
                    }
                    if (questRegistry.TryGetQuest(qid, out Model.Quest? quest))
                    {
                        _supported += 1;
                        if (questFunctions.IsQuestComplete(quest.Id))
                            _completed += 1;
                    }
                    //else
                    //{
                    //    ImGui.TableNextRow();
                    //    ImGui.TableNextColumn();
                    //    ImGui.TreeNodeEx(_LF("{0} ({1})", q.Name, (ushort)q.RowId),
                    //        ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth);

                    //    ImGui.TableNextColumn();
                    //    QuestJournalComponent.DrawCount(0, 0);
                    //    ImGui.TableNextColumn();
                    //    (Vector4 color, FontAwesomeIcon icon, string text) = uiUtils.GetQuestStyle(qid);
                    //    uiUtils.ChecklistItem(text, color, icon);
                    //}
                    if (qInfo != null && !questFunctions.IsQuestUnobtainable(qid))
                        _total += 1;
                }
                if (!_redoCount.TryGetValue(chapter, out var _result) || _result.Supported != _supported || _result.Completed != _completed)
                    _redoCount[chapter] = (_supported > _total ? _total : _supported, _completed, _total);
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
