namespace Questionable.Windows.JournalComponents;

[RegisterSingleton]
internal sealed class JournalTable(
    QuestRegistry questRegistry,
    QuestFunctions questFunctions,
    UiUtils uiUtils,
    QuestTooltipComponent questTooltipComponent,
    IDalamudPluginInterface pluginInterface,
    QuestJournalUtils questJournalUtils,
    QuestValidator questValidator,
    QuestController questController,
    RedoUtil redoUtil,
    Configuration configuration)
{
    private const int StaleDays = 7;

    // Journal tables are never nested, so the open table's optional columns are kept here for its rows.
    private bool _repColumn;
    private bool _markStale;

    public static EPathSupport GetSupport(bool removed, QuestRoot? root, IReadOnlyCollection<EIssueSeverity> issues)
    {
        if (removed)
            return EPathSupport.Removed;
        if ((root?.Comment ?? "").Contains("FATE", StringComparison.Ordinal))
            return EPathSupport.Fate;
        if (root is not { Disabled: false })
            return EPathSupport.Missing;
        if (issues.Contains(EIssueSeverity.Error))
            return EPathSupport.LoadError;
        return issues.Count > 0 ? EPathSupport.Issues : EPathSupport.Supported;
    }

    // A path nobody has reported working within a week, for path checkers.
    public static bool IsStale(QuestRoot? root, DateTime now) =>
        root != null && (root.LastChecked.Date == null || root.LastChecked.Since(now)!.Value.TotalDays > StaleDays);

    public ImRaii.TableDisposable Begin(string id, bool repColumn = false, bool markStale = false)
    {
        _repColumn = repColumn;
        _markStale = markStale;
        ImRaii.TableDisposable table = ImRaii.Table(id, repColumn ? 7 : 6, ImGuiTableFlags.NoSavedSettings);
        if (!table)
            return table;

        float scale = ImGui.GetIO().FontGlobalScale;
        ImGui.TableSetupColumn(_L("Name"), ImGuiTableColumnFlags.NoHide);
        ImGui.TableSetupColumn(_L("Level"), ImGuiTableColumnFlags.WidthFixed, 45 * scale);
        ImGui.TableSetupColumn(_L("XP"), ImGuiTableColumnFlags.WidthFixed, 90 * scale);
        ImGui.TableSetupColumn(_L("Gil"), ImGuiTableColumnFlags.WidthFixed, 75 * scale);
        if (repColumn)
            ImGui.TableSetupColumn(_L("Reputation"), ImGuiTableColumnFlags.WidthFixed, 70 * scale);
        ImGui.TableSetupColumn(_L("Supported"), ImGuiTableColumnFlags.WidthFixed, 100 * scale);
        ImGui.TableSetupColumn(_L("Completed"), ImGuiTableColumnFlags.WidthFixed, 100 * scale);
        ImGui.TableHeadersRow();
        return table;
    }

    // For header rows (section, rank, item group): the caller draws the name cell, then optionally the counts.
    public static void BeginRow()
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
    }

    public void Counts(int supported, int total, int completed, int obtainable)
    {
        SupportedCell();
        DrawCount(supported, total);
        ImGui.TableNextColumn();
        DrawCount(completed, obtainable);
    }

    // Moves a header row past the reward columns to Supported.
    public void SupportedCell()
    {
        for (int i = _repColumn ? 5 : 4; i > 0; --i)
            ImGui.TableNextColumn();
    }

    // label and unlocked replace the quest's name and completion, for rows that stand for a quest reward.
    public void QuestRow(IQuestInfo questInfo, string source, string? label = null, bool? unlocked = null)
    {
        questRegistry.TryGetQuest(questInfo.QuestId, out Quest? quest);

        BeginRow();
        ImGui.TreeNodeEx(label ?? GetName(questInfo, quest),
            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            DrawTooltip(questInfo, label != null);
        }

        if (ImGui.IsItemClicked() && questInfo is QuestInfo)
            questJournalUtils.MoveToIssuer(questInfo);

        questJournalUtils.ShowContextMenu(questInfo, quest, source);
        DrawPriorityMarker(quest);

        QuestInfo? info = questInfo as QuestInfo;
        ImGui.TableNextColumn();
        if (info != null)
            ImGui.TextUnformatted(info.Level.ToString(CultureInfo.CurrentCulture));
        ImGui.TableNextColumn();
        if (info is { Experience: > 0 })
            ImGui.TextUnformatted(info.Experience.ToString("N0", CultureInfo.CurrentCulture));
        ImGui.TableNextColumn();
        if (info is { Gil: > 0 })
            ImGui.TextUnformatted(info.Gil.ToString("N0", CultureInfo.CurrentCulture));

        if (_repColumn)
        {
            ImGui.TableNextColumn();
            if (questInfo is QuestInfo { SocietyRepValue: > 0 } society)
                ImGui.TextUnformatted($"+{society.SocietyRepValue}");
        }

        ImGui.TableNextColumn();
        DrawSupport(questInfo, quest);
        ImGui.TableNextColumn();
        DrawCompletion(questInfo, unlocked);
    }

    internal static void DrawCount(int count, int total)
    {
        string widest = 9999.ToString(CultureInfo.CurrentCulture);
        using var monoFont = ImRaii.PushFont(UiBuilder.MonoFont);

        if (total == 0)
            ImGui.TextColored(QstTheme.TextMuted, $"{" ".PadLeft(widest.Length)} - {" ".PadRight(widest.Length)}");
        else if (count == 0)
        {
            ImGui.TextUnformatted($"{"-".PadLeft(widest.Length)} / {total.ToString(CultureInfo.CurrentCulture).PadRight(widest.Length)}?");
        }
        else
        {
            string text =
                $"{count.ToString(CultureInfo.CurrentCulture).PadLeft(widest.Length)} / {total.ToString(CultureInfo.CurrentCulture).PadRight(widest.Length)}";
            if (count == total)
                ImGui.TextColored(QstTheme.Success, text);
            else
                ImGui.TextUnformatted(text);
        }
    }

    private string GetName(IQuestInfo questInfo, Quest? quest)
    {
        string name = $"{questInfo.Name} ({questInfo.QuestId})";
        if (quest is { Id: QuestId } && redoUtil.GetChapter(quest.Id.Value) is { Index: 0 } redoIndex)
            name = $"{name}   ({redoIndex.Chapter.ChapterName})";
        return name;
    }

    private void DrawTooltip(IQuestInfo questInfo, bool reward)
    {
        if (!reward)
        {
            questTooltipComponent.Draw(questInfo);
            return;
        }

        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
        ImGui.Text(_LF("Obtained from: {0}", questInfo.Name));
        using (ImRaii.PushIndent())
            questTooltipComponent.DrawInner(questInfo, showItemRewards: false);
    }

    private void DrawPriorityMarker(Quest? quest)
    {
        if (quest == null)
            return;

        bool acceptOnly = questController.PriorityManager.IsAcceptOnly(quest.Id);
        if (!acceptOnly && !questController.PriorityManager.Contains(quest))
            return;

        bool accepted = acceptOnly && questFunctions.IsQuestAccepted(quest.Id);
        ImGui.SameLine();
        using (pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            ImGui.TextColored(
                acceptOnly ? (accepted ? QstTheme.Success : QstTheme.Accent) : QstTheme.Amber,
                (acceptOnly ? FontAwesomeIcon.Inbox : FontAwesomeIcon.AngleDoubleUp).ToIconString());
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(acceptOnly
                ? (accepted ? _L("Accepted; completion follows the normal quest order.") : _L("Queued to be accepted"))
                : _L("This quest is in Priority Quests."));
    }

    private void DrawSupport(IQuestInfo questInfo, Quest? quest)
    {
        string lastChecked = "";
        string lastCheckedLong = "";
        if (quest?.Root.LastChecked.Date != null)
        {
            lastCheckedLong = "\n" + _LF("Last checked: {0}", quest.Root.LastChecked);
            int daysSince = (int)quest.Root.LastChecked.Since(DateTime.Now)!.Value.TotalDays;
            if (daysSince >= 0 && daysSince < 7)
                lastChecked = $"{daysSince}d";
            else if (daysSince >= 7)
                lastChecked = $"{daysSince / 7}w";
        }

        float spacing;
        using (pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            spacing = ImGui.GetColumnWidth() / 2 - ImGui.CalcTextSize(FontAwesomeIcon.Check.ToIconString()).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + spacing);

        string defaultReason = _L("<no reason specified>");
        string reason = quest != null ? (quest.Root.Comment ?? defaultReason).Split('\n', 2)[0] : defaultReason;
        string addendum = lastCheckedLong + (!reason.Equals(defaultReason, StringComparison.Ordinal) ? "\n" + _LF("Reason: {0}", reason) : "");

        List<EIssueSeverity> issues = quest is { Root.Disabled: false }
            ? [.. questValidator.GetIssues(quest.Id).Select(x => x.Severity)]
            : [];
        switch (GetSupport(QuestFunctions.IsQuestRemoved(questInfo.QuestId), quest?.Root, issues))
        {
            case EPathSupport.Removed:
                if (uiUtils.ChecklistItem(lastChecked, QstTheme.TextMuted, FontAwesomeIcon.Minus))
                    ImGui.SetTooltip(_L("This quest is not available.") + addendum);
                break;
            case EPathSupport.Fate:
                if (uiUtils.ChecklistItem(lastChecked, QstTheme.Accent, FontAwesomeIcon.ExclamationTriangle))
                    ImGui.SetTooltip(_L("This quest requires completing a FATE.") + addendum);
                break;
            case EPathSupport.LoadError:
                if (uiUtils.ChecklistItem(lastChecked, QstTheme.Danger, FontAwesomeIcon.ExclamationTriangle))
                    ImGui.SetTooltip(_L("This quest could not be loaded.") + addendum);
                break;
            case EPathSupport.Issues:
                if (uiUtils.ChecklistItem(lastChecked, QstTheme.Info, FontAwesomeIcon.InfoCircle))
                    ImGui.SetTooltip(_L("This quest had validation issues.") + addendum);
                break;
            case EPathSupport.Supported:
                bool stale = _markStale && IsStale(quest?.Root, DateTime.Now);
                if (uiUtils.ChecklistItem(lastChecked, complete: true, stale ? QstTheme.Accent : null))
                    ImGui.SetTooltip(_L("This quest is supported.") + addendum);
                break;
            default:
                if (uiUtils.ChecklistItem(lastChecked, complete: false))
                    ImGui.SetTooltip(_L("This quest is not yet supported.") + addendum);
                break;
        }

        if (configuration.Stop.QuestsToStopWhenAccepted.Contains(questInfo.QuestId) ||
            configuration.Stop.QuestsToStopAfter.Contains(questInfo.QuestId))
        {
            ImGui.SameLine();
            using (pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                ImGui.TextColored(QstTheme.Amber, FontAwesomeIcon.StopCircle.ToIconString());
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("This quest is in Stop Conditions."));
        }
    }

    private void DrawCompletion(IQuestInfo questInfo, bool? unlocked)
    {
        if (unlocked is { } done)
        {
            if (uiUtils.ChecklistItem("", done))
                ImGui.SetTooltip(done ? _L("Unlocked") : _L("Not unlocked yet"));
            return;
        }

        (Vector4 color, FontAwesomeIcon icon, string text) = uiUtils.GetQuestStyle(questInfo.QuestId);
        uint? iconOverride = questInfo is QuestInfo info ? QuestJournalUtils.GetIconOverride(info, icon) : null;
        if (uiUtils.ChecklistItem(text.Split(':')[0], color, icon, iconOverride: iconOverride))
            ImGui.SetTooltip(text);
    }
}
