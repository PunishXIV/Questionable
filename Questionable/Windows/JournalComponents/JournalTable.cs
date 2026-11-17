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
    QuestReporter questReporter,
    Configuration configuration)
{
    // Journal tables are never nested, so the open table's optional columns are kept here for its rows.
    private bool _repColumn;

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

    // pinHeader makes the table scroll instead of the window, so the header row stays in view.
    // Only for a table that is the last thing on its tab: it takes the remaining height.
    public ImRaii.TableDisposable Begin(string id, bool repColumn = false,
        bool pinHeader = false)
    {
        _repColumn = repColumn;
        ImGuiTableFlags flags = ImGuiTableFlags.NoSavedSettings;
        if (pinHeader)
            flags |= ImGuiTableFlags.ScrollY;
        ImRaii.TableDisposable table = ImRaii.Table(id, repColumn ? 7 : 6, flags);
        if (!table)
            return table;

        if (pinHeader)
            ImGui.TableSetupScrollFreeze(0, 1);

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

    // A group row's Level, XP and Gil cells are empty, so its name may run across them
    // instead of being cut off at the Name column.
    public WideNameEnd WideName()
    {
        ImGui.TableSetColumnIndex(_repColumn ? 5 : 4);
        float right = ImGui.GetCursorScreenPos().X - ImGui.GetStyle().CellPadding.X;
        ImGui.TableSetColumnIndex(0);

        // Keeps the top and bottom of the current clip, so rows still slide under a pinned header.
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        ImGui.PushClipRect(drawList.GetClipRectMin(), new Vector2(right, drawList.GetClipRectMax().Y), false);
        return default;
    }

    internal readonly struct WideNameEnd : IDisposable
    {
        public void Dispose() => ImGui.PopClipRect();
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
        ReportStatus? status = quest != null ? questReporter.StatusFor(quest) : null;

        float spacing;
        using (pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            spacing = ImGui.GetColumnWidth() / 2 - ImGui.CalcTextSize(FontAwesomeIcon.Check.ToIconString()).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + spacing);

        string defaultReason = _L("<no reason specified>");
        string reason = quest != null ? (quest.Root.Comment ?? defaultReason).Split('\n', 2)[0] : defaultReason;
        string addendum = !reason.Equals(defaultReason, StringComparison.Ordinal) ? "\n" + _LF("Reason: {0}", reason) : "";

        List<EIssueSeverity> issues = quest is { Root.Disabled: false }
            ? [.. questValidator.GetIssues(quest.Id).Select(x => x.Severity)]
            : [];
        switch (GetSupport(QuestFunctions.IsQuestRemoved(questInfo.QuestId), quest?.Root, issues))
        {
            case EPathSupport.Removed:
                if (uiUtils.ChecklistItem("", QstTheme.TextMuted, FontAwesomeIcon.Minus))
                    ImGui.SetTooltip(_L("This quest is not available.") + addendum);
                break;
            case EPathSupport.Fate:
                if (uiUtils.ChecklistItem("", QstTheme.Accent, FontAwesomeIcon.ExclamationTriangle))
                    ImGui.SetTooltip(_L("This quest requires completing a FATE.") + addendum);
                break;
            case EPathSupport.LoadError:
                if (uiUtils.ChecklistItem("", QstTheme.Danger, FontAwesomeIcon.ExclamationTriangle))
                    ImGui.SetTooltip(_L("This quest could not be loaded.") + addendum);
                break;
            case EPathSupport.Issues:
                if (uiUtils.ChecklistItem("", QstTheme.Info, FontAwesomeIcon.InfoCircle))
                    ImGui.SetTooltip(_L("This quest had validation issues.") + addendum);
                break;
            case EPathSupport.Supported:
                if (uiUtils.ChecklistItem("", complete: true))
                    ImGui.SetTooltip(_L("This quest is supported.") + addendum);
                break;
            default:
                if (uiUtils.ChecklistItem("", complete: false))
                    ImGui.SetTooltip(_L("This quest is not yet supported.") + addendum);
                break;
        }

        if (status is { Reason: not EReportReason.Disabled })
        {
            ImGui.SameLine();
            using (pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                ImGui.TextColored(ReportUi.Color(status), FontAwesomeIcon.Circle.ToIconString());
            if (ImGui.IsItemHovered())
                ReportUi.Tooltip(ReportUi.Description(status));
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
