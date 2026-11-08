using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Lumina.Excel.Sheets;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows.JournalComponents;

[RegisterSingleton]
internal sealed class RedoComponent
(
    RedoUtil redoUtil,
    QuestController questController,
    QuestJournalComponent questJournalComponent,
    QuestJournalUtils questJournalUtils,
    QuestTooltipComponent questTooltipComponent,
    QuestData questData,
    QuestRegistry questRegistry,
    QuestFunctions questFunctions,
    Configuration configuration,
    UiUtils uiUtils)
{
    private bool _hideDone;
    private readonly Dictionary<QuestRedoChapterUI, (int Supported, int Completed, int Total)> _counts = [];
    private string _filter = "";
    public void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("New Game+"));
        if (!tab)
            return;

        // Prevent ng+ activation if ng+ unlock quest is incomplete
        if (redoUtil.IsLocked)
        {
            bool locked = true;
            if (redoUtil.UnlockQuest != null)
                (locked, var _) = questFunctions.IsQuestLocked(redoUtil.UnlockQuest.Id);
            using var _ = ImRaii.Disabled(locked);
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Play, _L("Unlock NG+")))
            {
                questController.SetNextQuest(redoUtil.UnlockQuest);
                questController.StartSingleQuest("Unlock NG+");
            }
            if (ImGui.IsItemHovered() && redoUtil.UnlockQuest != null)
                questTooltipComponent.Draw(redoUtil.UnlockQuest.Info);
            ImGui.SameLine();
            if (redoUtil.UnlockQuest == null)
                ImGui.Text(_L("Loading..."));
            else
                ImGui.Text(_L("New Game+ has not been unlocked on this character"));
        }

        using (ImRaii.Disabled(!redoUtil.IsActive()))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Stop, _L("Stop NG+")))
                if (!redoUtil.IsLocked)
                    redoUtil.SendCommand(redoChapter: RedoChapter.Off);
        }
        if (configuration.Advanced.Debug)
        {
            ImGui.SameLine();
            if (ImGuiComponentsLocal.IconButton(_hideDone ? FontAwesomeIcon.ChevronRight : FontAwesomeIcon.ChevronDown,
                _hideDone ? QstTheme.Accent : null))
            {
                _hideDone = !_hideDone;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Hide chapters that have been completely checked"));
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(_L("Quests marked with orange need to be reported as working or not via the LastChecked system. Ask Aly for more details!"),
                                   FontAwesomeIcon.InfoCircle, QstTheme.Accent);
        ImGui.SameLine();
        ImGui.Text(_L("Active:"));
        ImGui.SameLine();
        redoUtil.TryGetActiveChapter(out var activeChapter);
        ImGui.Text(activeChapter?.ChapterName.ToString() ?? ("None"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(230 * ImGui.GetIO().FontGlobalScale);
        _ = ImGui.InputTextWithHint("###RedoComponentFilter", _L("Filter NG+ categories"), ref _filter, maxLength: 20);

        using ImRaii.TableDisposable table = ImRaii.Table("RedoTable", 3, ImGuiTableFlags.NoSavedSettings);
        if (!table)
            return;

        ImGui.TableSetupColumn(_L("Name"), ImGuiTableColumnFlags.NoHide);
        ImGui.TableSetupColumn(_L("Supported"), ImGuiTableColumnFlags.WidthFixed, 100 * ImGui.GetIO().FontGlobalScale);
        ImGui.TableSetupColumn(_L("Completed"), ImGuiTableColumnFlags.WidthFixed, 100 * ImGui.GetIO().FontGlobalScale);
        ImGui.TableHeadersRow();
        foreach ((QuestRedoChapterUI chapter, RedoCache redoCache) in redoUtil.Chapters)
        {
            var questCount = redoCache.Quests.Count;
            if (questCount == 0)
                continue;
            var chapterName = redoCache.Chapter.ChapterName.ToString() ?? "";
            chapterName = chapterName.Length > 0 ? chapterName : _L("???");
            string? categoryName = redoCache.Chapter.UITab.Value.Text.ToString();
            categoryName = categoryName != null && categoryName.Length > 0 ? $"{categoryName}: " : "";
            if (!_filter.IsNullOrEmpty() &&
                !chapterName.Contains(_filter, StringComparison.InvariantCultureIgnoreCase) &&
                !categoryName.Contains(_filter, StringComparison.InvariantCultureIgnoreCase))
                continue;

            bool showAnyway = false;
            var uncheckedQuests = redoCache.Quests.Select(q =>
            {
                var questId = new QuestId((ushort)q.RowId);
                bool unobtainable = questData.TryGetQuestInfo(questId, out var info) && questFunctions.IsQuestUnobtainable(questId);
                questRegistry.TryGetQuest(questId, out Domain.Quest? quest);
                if (info != null && quest != null && (quest.Root.LastChecked.Date == null ||
                        (quest.Root.LastChecked.Date != null &&
                         quest.Root.LastChecked.Since(DateTime.Now)!.Value.TotalDays > 60)) &&
                        !unobtainable)
                    return quest;
                if (!unobtainable && info != null && quest == null)
                    showAnyway = true;
                return null;
            }).OfType<Domain.Quest>().ToArray();
            if (uncheckedQuests.Length == 0 && _hideDone && !showAnyway)
                continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImRaii.ColorDisposable? color = null;
            if (uncheckedQuests.Length > 0)
                color = ImRaii.PushColor(ImGuiCol.Text, QstTheme.Accent);
            bool open = ImGui.TreeNodeEx($"{chapter.RowId}", ImGuiTreeNodeFlags.SpanFullWidth, $"{categoryName}{chapterName}");
            color?.Dispose();
            if (uncheckedQuests.Length > 0 && uncheckedQuests[0] != null && ImGui.IsItemHovered())
            {
                using var _ = ImRaii.Tooltip();
                var index = redoUtil.GetChapter(uncheckedQuests[0]!.Id.Value);
                ImGui.Text(_LF("({0}) Unchecked: #{1}{2} ({3}/{4})",
                    chapter.RowId, index.SimplifiedIndex, (uncheckedQuests.Length > 1 ? "+" : ""), uncheckedQuests.Length, redoCache.Quests.Count));
                using var __ = ImRaii.PushColor(ImGuiCol.Text, QstTheme.Accent);
                ImGui.Text(string.Join('\n', uncheckedQuests.Select(q => $"{q?.Info.SimplifiedName} ({q?.Id})")));
            }

            ShowGroupContextMenu($"DrawRedoChapter{chapter.RowId}",
                redoCache.Quests.Select(q =>
                {
                    questData.TryGetQuestInfo(new QuestId((ushort)q.RowId), out IQuestInfo? info);
                    return info;
                }).OfType<IQuestInfo>().ToList(), redoCache, categoryName.StartsWith("???") || chapterName.StartsWith("???"));

            if (_counts.TryGetValue(chapter, out (int Supported, int Completed, int Total) result))
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
                QuestJournalComponent.DrawCount(0, questCount);
            }
            if (open)
            {
                int supported = 0;
                int completed = 0;
                int total = 0;
                foreach (var q in redoCache.Quests)
                {
                    var questId = new QuestId((ushort)q.RowId);
                    questData.TryGetQuestInfo(questId, out IQuestInfo? info);
                    if (info != null)
                        questJournalComponent.DrawQuest(info);
                    else
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TreeNodeEx($"{q.Name} ({(ushort)q.RowId})",
                            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth);
                        ImGui.TableNextColumn();
                        if (uiUtils.ChecklistItem("", QstTheme.TextMuted, FontAwesomeIcon.Minus))
                            ImGui.SetTooltip(_L("This quest is not supported."));
                        ImGui.TableNextColumn();
                    }
                    if (questRegistry.TryGetQuest(questId, out Domain.Quest? quest))
                    {
                        supported += 1;
                        if (questFunctions.IsQuestComplete(quest.Id))
                            completed += 1;
                    }
                    if (info != null && !questFunctions.IsQuestUnobtainable(questId))
                        total += 1;
                }
                if (!_counts.TryGetValue(chapter, out var cached) || cached.Supported != supported || cached.Completed != completed)
                    _counts[chapter] = (supported > total ? total : supported, completed, total);
                ImGui.TreePop();
            }
        }
    }

    public void ShowGroupContextMenu(string id, List<IQuestInfo> quests, RedoCache redoCache, bool hideStart)
    {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup($"##QuestGroupPopup{id}");

        using ImRaii.PopupDisposable popup = ImRaii.Popup($"##QuestGroupPopup{id}");
        if (!popup)
            return;

        questJournalUtils.DrawPriorityItems(quests);

        if (ImGui.MenuItem(_L("Sim first quest")))
            if (quests.Count >= 1)
                questController.SimulateQuest(quests[0], 0, 0);

        if (configuration.Advanced.Debug && !hideStart)
        {
            bool redoActive = redoUtil.IsActive();
            using (ImRaii.Disabled(redoActive))
            {
                if (ImGui.MenuItem(_L("Start NG+ here")) && redoCache.Chapter.RowId != 0)
                {
                    if (!redoUtil.IsLocked)
                        if (redoActive) // safeguard
                            redoUtil.SendCommand(redoChapter: RedoChapter.Off);
                        else
                            redoUtil.SendCommand(questRedoChapter: redoCache.Chapter);
                }
            }
            if (redoActive && ImGui.MenuItem(_L("Stop NG+")))
                if (!redoUtil.IsLocked)
                    redoUtil.SendCommand(redoChapter: RedoChapter.Off);
        }

    }

}
