using Lumina.Excel.Sheets;
namespace Questionable.Windows.JournalComponents;

[RegisterSingleton]
internal sealed class RedoComponent
(
    RedoUtil redoUtil,
    QuestController questController,
    QuestStarter questStarter,
    JournalTable journalTable,
    QuestJournalUtils questJournalUtils,
    QuestTooltipComponent questTooltipComponent,
    QuestData questData,
    QuestRegistry questRegistry,
    QuestFunctions questFunctions,
    Configuration configuration,
    QuestReporter questReporter,
    UiUtils uiUtils)
{
    private bool _hideDone;
    private readonly Dictionary<QuestRedoChapterUI, (int Supported, int Completed, int Total)> _counts = [];
    private string _filter = "";
    public void Draw()
    {
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
                questStarter.StartSingleQuest("Unlock NG+");
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
                QstWidgets.Tooltip(_L("Hide chapters whose paths all have current working reports or admin verification"));
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(_L("Red has unresolved problems; orange needs more or newer reports; green is recently reported or verified working; grey has no reports or unavailable data. Hover a chapter for details."),
                                   FontAwesomeIcon.InfoCircle, QstTheme.Accent);
        ImGui.SameLine();
        ImGui.Text(_L("Active:"));
        ImGui.SameLine();
        redoUtil.TryGetActiveChapter(out var activeChapter);
        ImGui.Text(activeChapter?.ChapterName.ToString() ?? ("None"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(230 * ImGui.GetIO().FontGlobalScale);
        _ = ImGui.InputTextWithHint("###RedoComponentFilter", _L("Filter NG+ categories"), ref _filter, maxLength: 20);

        using ImRaii.TableDisposable table = journalTable.Begin("RedoTable", pinHeader: true);
        if (!table)
            return;

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
            List<(string Name, ReportStatus Status)> reports = [];
            foreach (var q in redoCache.Quests)
            {
                var questId = new QuestId((ushort)q.RowId);
                bool unobtainable = questData.TryGetQuestInfo(questId, out var info) && questFunctions.IsQuestUnobtainable(questId);
                questRegistry.TryGetQuest(questId, out Domain.Quest? quest);
                if (unobtainable || info == null)
                    continue;
                if (quest is not { Root.Disabled: false })
                    showAnyway = true;
                else
                    reports.Add((info.Name, questReporter.StatusFor(quest)));
            }
            ReportStatus group = ReportStatus.Worst(reports.Select(x => x.Status));
            if ((reports.Count == 0 || group.State == EReportState.Working) && _hideDone && !showAnyway)
                continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImRaii.ColorDisposable? color = null;
            if (AlliedSocietyComponent.HeaderColor(true, configuration.Reports.ShowCounts, showAnyway, group.State,
                    incomplete: false) is { } chapterColor)
                color = ImRaii.PushColor(ImGuiCol.Text, chapterColor);
            bool open;
            using (journalTable.WideName())
                open = ImGui.TreeNodeEx($"{chapter.RowId}", ImGuiTreeNodeFlags.SpanFullWidth, $"{categoryName}{chapterName}");
            color?.Dispose();
            if (ImGui.IsItemHovered() && (reports.Count > 0 || showAnyway))
            {
                string details = ReportUi.GroupDescription(reports);
                if (showAnyway)
                    details += "\n" + _L("This chapter also has missing or disabled paths.");
                QstWidgets.Tooltip(details);
            }

            ShowGroupContextMenu($"DrawRedoChapter{chapter.RowId}",
                redoCache.Quests.Select(q =>
                {
                    questData.TryGetQuestInfo(new QuestId((ushort)q.RowId), out IQuestInfo? info);
                    return info;
                }).OfType<IQuestInfo>().ToList(), redoCache, categoryName.StartsWith("???") || chapterName.StartsWith("???"));

            if (_counts.TryGetValue(chapter, out (int Supported, int Completed, int Total) result))
                journalTable.Counts(result.Supported, result.Total, result.Completed, result.Total);
            else
                journalTable.Counts(0, 0, 0, questCount);
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
                        journalTable.QuestRow(info, nameof(RedoComponent));
                    else
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TreeNodeEx($"{q.Name} ({(ushort)q.RowId})",
                            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth);
                        journalTable.SupportedCell();
                        if (uiUtils.ChecklistItem("", QstTheme.TextMuted, FontAwesomeIcon.Minus))
                            QstWidgets.Tooltip(_L("This quest isn't supported yet."));
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

        if (configuration.Advanced.Debug && ImGui.MenuItem(_L("Sim first quest")))
            if (quests.Count >= 1)
                questStarter.SimulateQuest(quests[0], 0, 0);

        if (configuration.Advanced.Debug && !hideStart)
        {
            bool redoActive = redoUtil.IsActive();
            using (ImRaii.Disabled(redoActive))
            {
                if (ImGui.MenuItem(_L("Start NG+ here")) && redoCache.Chapter.RowId != 0)
                {
                    if (!redoUtil.IsLocked)
                        if (redoActive)
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
