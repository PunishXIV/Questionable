using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Validation;
using Questionable.Windows.QuestComponents;
using Questionable.Windows.Utils;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable.Windows.JournalComponents;

internal sealed class QuestJournalComponent
(
    JournalData journalData,
    QuestRegistry questRegistry,
    QuestFunctions questFunctions,
    UiUtils uiUtils,
    QuestTooltipComponent questTooltipComponent,
    IDalamudPluginInterface pluginInterface,
    QuestJournalUtils questJournalUtils,
    QuestValidator questValidator,
    QuestController questController,
    RedoUtil redoUtil)
{
    private readonly Dictionary<JournalData.Category, JournalCounts> _categoryCounts = [];
    private readonly Dictionary<JournalData.Genre, JournalCounts> _genreCounts = [];
    private readonly Dictionary<JournalData.Section, JournalCounts> _sectionCounts = [];

    private List<FilteredSection> _filteredSections = [];

    internal FilterConfiguration Filter { get; } = new();

    public void DrawQuests()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Quests"));
        if (!tab)
            return;

        if (ImGui.CollapsingHeader(_L("Explanation")))
        {
            ImGui.Text(_L("The list below contains all quests that appear in your journal."));
            ImGui.BulletText(_L("'Supported' lists quests that Questionable can do for you"));
            ImGui.BulletText(_L("'Completed' lists quests your current character has completed."));
            ImGui.BulletText(
                _L("Not all quests can be completed even if they're listed as available, e.g. starting city quest chains."));
            ImGui.BulletText(_L("The text in the Supported column indicates the last time a quest path was reported to work perfectly."));
            ImGui.TextColoredWrapped(ImGuiColors.DalamudYellow, _L("Quests can be added to Priority Quests, either individually or by group, with the right click menu."));

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        QuestJournalUtils.ShowFilterContextMenu(this);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputTextWithHint(string.Empty, _L("Search quests and categories"), ref Filter.SearchText, 256))
            UpdateFilter();

        if (_filteredSections.Count > 0)
        {
            using ImRaii.TableDisposable table = ImRaii.Table("Quests", 3, ImGuiTableFlags.NoSavedSettings);
            if (!table)
                return;

            ImGui.TableSetupColumn(_L("Name"), ImGuiTableColumnFlags.NoHide);
            ImGui.TableSetupColumn(_L("Supported"), ImGuiTableColumnFlags.WidthFixed, 100 * ImGui.GetIO().FontGlobalScale);
            ImGui.TableSetupColumn(_L("Completed"), ImGuiTableColumnFlags.WidthFixed, 100 * ImGui.GetIO().FontGlobalScale);
            ImGui.TableHeadersRow();

            foreach (FilteredSection section in _filteredSections)
                DrawSection(section);
        }
        else
            ImGui.Text(_L("No quest or category matches your search."));
    }

    private void DrawSection(FilteredSection filter)
    {
        (int available, int total, int obtainable, int completed) =
            _sectionCounts.GetValueOrDefault(filter.Section, new());
        if (total == 0)
            return;

        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        bool open = ImGui.TreeNodeEx(filter.Section.Name, ImGuiTreeNodeFlags.SpanFullWidth);

        ImGui.TableNextColumn();
        DrawCount(available, total);
        ImGui.TableNextColumn();
        DrawCount(completed, obtainable);

        if (open)
        {
            foreach (FilteredCategory category in filter.Categories)
                DrawCategory(category);

            ImGui.TreePop();
        }
    }

    private void DrawCategory(FilteredCategory filter)
    {
        (int available, int total, int obtainable, int completed) =
            _categoryCounts.GetValueOrDefault(filter.Category, new());
        if (total == 0)
            return;

        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        bool open = ImGui.TreeNodeEx(filter.Category.Name, ImGuiTreeNodeFlags.SpanFullWidth);

        ImGui.TableNextColumn();
        DrawCount(available, total);
        ImGui.TableNextColumn();
        DrawCount(completed, obtainable);

        if (open)
        {
            foreach (FilteredGenre genre in filter.Genres)
                DrawGenre(genre);

            ImGui.TreePop();
        }
    }

    private void DrawGenre(FilteredGenre filter)
    {
        (int supported, int total, int obtainable, int completed) = _genreCounts.GetValueOrDefault(filter.Genre, new());
        if (total == 0)
            return;

        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        string genreName = filter.Genre.Name;
        if (questRegistry.TryGetQuest(filter.Quests.First().QuestId, out Quest? q))
        {
            RedoIndex redoIndex = redoUtil.GetChapter(q.Id.Value);
            if (redoIndex.Index != -1)
                genreName = $"{filter.Genre.Name} ({redoIndex.Chapter.ChapterName})";
        }

        bool open = ImGui.TreeNodeEx(genreName, ImGuiTreeNodeFlags.SpanFullWidth);

        questJournalUtils.ShowQuestGroupContextMenu($"DrawGenre{filter.Genre.Id}", filter.Quests);

        ImGui.TableNextColumn();
        DrawCount(supported, total);
        ImGui.TableNextColumn();
        DrawCount(completed, obtainable);

        if (open)
        {
            foreach (IQuestInfo quest in filter.Quests)
                DrawQuest(quest);

            ImGui.TreePop();
        }
    }

    internal void DrawQuest(IQuestInfo questInfo) => DrawQuest((QuestInfo)questInfo);

    internal void DrawQuest(QuestInfo questInfo)
    {
        bool fate = false;
        //bool repeatable = false;
        string lastChecked = "";
        string lastCheckedLong = "";
        string questDescription = $"{questInfo.Name} ({questInfo.QuestId})";
        if (questRegistry.TryGetQuest(questInfo.QuestId, out Quest? quest))
        {
            if (quest.Root.LastChecked.Date != null)
            {
                lastCheckedLong = "\n" + _LF("Last checked: {0}", quest.Root.LastChecked);
                int since = (int)quest.Root.LastChecked.Since(DateTime.Now)!.Value.TotalDays;
                if (since >= 0 && since < 7)
                    lastChecked = $"{since}d";
                else if (since >= 7)
                    lastChecked = $"{since / 7}w";
            }
            RedoIndex redoIndex = redoUtil.GetChapter(quest.Id.Value);
            if (redoIndex.Index == 0)
                questDescription = $"{questDescription}   ({redoIndex.Chapter.ChapterName})";

            if ((quest.Root.Comment ?? "").Contains("FATE"))
                fate = true;
            /*if ((quest.Root.Comment ?? "").Contains("Repeatable"))
            {
                repeatable = true;
            }*/
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TreeNodeEx(questDescription,
            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth);

        if (ImGui.IsItemHovered())
            questTooltipComponent.Draw(questInfo);

        if (ImGui.IsItemClicked())
            questJournalUtils.MoveToQuestLocation(questInfo);

        questJournalUtils.ShowContextMenu(questInfo, quest, nameof(QuestJournalComponent));

        if (quest != null && questController.PriorityManager.Contains(quest))
        {
            ImGui.SameLine();
            using (pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                ImGui.TextColored(ImGuiColors.DalamudYellow, FontAwesomeIcon.ExclamationCircle.ToIconString());
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("This quest is in Priority Quests."));
        }

        ImGui.TableNextColumn();
        float spacing;
        // ReSharper disable once UnusedVariable
        using (IDisposable font = pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            spacing = ImGui.GetColumnWidth() / 2 - ImGui.CalcTextSize(FontAwesomeIcon.Check.ToIconString()).X;
        }

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + spacing);
        string defaultReason;
        string reason = defaultReason = _L("<no reason specified>");
        if (quest != null)
            reason = (quest.Root.Comment ?? defaultReason).Split('\n', 2)[0];

        if (QuestFunctions.IsQuestRemoved(questInfo.QuestId))
        {
            if (uiUtils.ChecklistItem(lastChecked, ImGuiColors.DalamudGrey, FontAwesomeIcon.Minus))
                ImGui.SetTooltip(_L("This quest is not available."));
        }
        else if (fate)
        {
            if (uiUtils.ChecklistItem(lastChecked, ImGuiColors.DalamudOrange, FontAwesomeIcon.ExclamationTriangle))
                ImGui.SetTooltip(_L("This quest requires completing a FATE.") + lastCheckedLong);
        }
        else if (quest is { Root.Disabled: false })
        {
            List<ValidationIssue> issues = questValidator.GetIssues(quest.Id);
            if (issues.Any(x => x.Severity == EIssueSeverity.Error))
            {
                if (uiUtils.ChecklistItem(lastChecked, ImGuiColors.DalamudRed, FontAwesomeIcon.ExclamationTriangle))
                    ImGui.SetTooltip(_L("This quest could not be loaded."));
            }
            else if (issues.Count > 0)
            {
                if (uiUtils.ChecklistItem(lastChecked, ImGuiColors.ParsedBlue, FontAwesomeIcon.InfoCircle))
                    ImGui.SetTooltip(_L("This quest had validation issues."));
            }
            else if (uiUtils.ChecklistItem(lastChecked, true))
                ImGui.SetTooltip(_L("This quest is supported.") + lastCheckedLong + (!reason.Equals(defaultReason, StringComparison.Ordinal) ? "\n" + _LF("Comment: {0}", reason) : ""));
        }
        else
        {
            if (quest == null)
                reason = ("No quest path.");
            if (uiUtils.ChecklistItem(lastChecked, false))
                ImGui.SetTooltip(_L("This quest is not yet supported.") + lastCheckedLong + (!reason.Equals(defaultReason, StringComparison.Ordinal) ? "\n" + _LF("Reason: {0}", reason) : ""));
        }

        ImGui.TableNextColumn();
        (Vector4 color, FontAwesomeIcon icon, string text) = uiUtils.GetQuestStyle(questInfo.QuestId);
        uiUtils.ChecklistItem(text.Split(',',1)[0], color, icon);
    }

    internal static void DrawCount(int count, int total)
    {
        string len = 9999.ToString(CultureInfo.CurrentCulture);
        ImGui.PushFont(UiBuilder.MonoFont);

        if (total == 0)
            ImGui.TextColored(ImGuiColors.DalamudGrey, $"{" ".PadLeft(len.Length)} - {" ".PadRight(len.Length)}");
        else if (count == 0)
        {
            ImGui.TextUnformatted($"{"-".PadLeft(len.Length)} / {total.ToString(CultureInfo.CurrentCulture).PadRight(len.Length)}?");
        }
        else
        {
            string text =
                $"{count.ToString(CultureInfo.CurrentCulture).PadLeft(len.Length)} / {total.ToString(CultureInfo.CurrentCulture).PadRight(len.Length)}";
            if (count == total)
                ImGui.TextColored(ImGuiColors.ParsedGreen, text);
            else
                ImGui.TextUnformatted(text);
        }

        ImGui.PopFont();
    }

    public void UpdateFilter()
    {
        _filteredSections = journalData.Sections
            .Select(x => FilterSection(x, Filter))
            .Where(x => x.Categories.Count > 0)
            .ToList();

        RefreshCounts();
    }

    private FilteredSection FilterSection(JournalData.Section section, FilterConfiguration filter)
    {
        IEnumerable<FilteredCategory> filteredCategories;
        if (IsCategorySectionGenreMatch(filter, section.Name))
        {
            filteredCategories = section.Categories
                .Select(x => FilterCategory(x, filter.WithoutName()));
        }
        else
        {
            filteredCategories = section.Categories
                .Select(category => FilterCategory(category, filter));
        }

        return new(section, filteredCategories.Where(x => x.Genres.Count > 0).ToList());
    }

    private FilteredCategory FilterCategory(JournalData.Category category, FilterConfiguration filter)
    {
        IEnumerable<FilteredGenre> filteredGenres;
        if (IsCategorySectionGenreMatch(filter, category.Name))
        {
            filteredGenres = category.Genres
                .Select(x => FilterGenre(x, filter.WithoutName()));
        }
        else
        {
            filteredGenres = category.Genres
                .Select(genre => FilterGenre(genre, filter));
        }

        return new(category, filteredGenres.Where(x => x.Quests.Count > 0).ToList());
    }

    private FilteredGenre FilterGenre(JournalData.Genre genre, FilterConfiguration filter)
    {
        IEnumerable<IQuestInfo> filteredQuests;
        if (IsCategorySectionGenreMatch(filter, genre.Name))
        {
            filteredQuests = genre.Quests
                .Where(x => IsQuestMatch(filter.WithoutName(), x));
        }
        else
        {
            filteredQuests = genre.Quests
                .Where(x => IsQuestMatch(filter, x));
        }

        return new(genre, filteredQuests.ToList());
    }

    internal void RefreshCounts()
    {
        _genreCounts.Clear();
        _categoryCounts.Clear();
        _sectionCounts.Clear();

        foreach (JournalData.Genre genre in journalData.Genres)
        {
            int available = genre.Quests.Count(x =>
                questRegistry.TryGetQuest(x.QuestId, out Quest? quest) &&
                !quest.Root.Disabled &&
                !QuestFunctions.IsQuestRemoved(x.QuestId));
            int total = genre.Quests.Count(x => !QuestFunctions.IsQuestRemoved(x.QuestId));
            int obtainable = genre.Quests.Count(x => !questFunctions.IsQuestUnobtainable(x.QuestId));
            int completed = genre.Quests.Count(x => questFunctions.IsQuestComplete(x.QuestId));
            _genreCounts[genre] = new(available, total, obtainable, completed);
        }

        foreach (JournalData.Category category in journalData.Categories)
        {
            List<JournalCounts> counts = _genreCounts
                .Where(x => category.Genres.Contains(x.Key))
                .Select(x => x.Value)
                .ToList();
            int available = counts.Sum(x => x.Available);
            int total = counts.Sum(x => x.Total);
            int obtainable = counts.Sum(x => x.Obtainable);
            int completed = counts.Sum(x => x.Completed);
            _categoryCounts[category] = new(available, total, obtainable, completed);
        }

        foreach (JournalData.Section section in journalData.Sections)
        {
            List<JournalCounts> counts = _categoryCounts
                .Where(x => section.Categories.Contains(x.Key))
                .Select(x => x.Value)
                .ToList();
            int available = counts.Sum(x => x.Available);
            int total = counts.Sum(x => x.Total);
            int obtainable = counts.Sum(x => x.Obtainable);
            int completed = counts.Sum(x => x.Completed);
            _sectionCounts[section] = new(available, total, obtainable, completed);
        }
    }

    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Required by LogoutDelegate signature")]
    internal void ClearCounts(int type, int code)
    {
        foreach (KeyValuePair<JournalData.Genre, JournalCounts> genreCount in _genreCounts.ToList())
            _genreCounts[genreCount.Key] = genreCount.Value with { Completed = 0 };

        foreach (KeyValuePair<JournalData.Category, JournalCounts> categoryCount in _categoryCounts.ToList())
            _categoryCounts[categoryCount.Key] = categoryCount.Value with { Completed = 0 };

        foreach (KeyValuePair<JournalData.Section, JournalCounts> sectionCount in _sectionCounts.ToList())
            _sectionCounts[sectionCount.Key] = sectionCount.Value with { Completed = 0 };
    }

    private static bool IsCategorySectionGenreMatch(FilterConfiguration filter, string name)
    {
        return string.IsNullOrEmpty(filter.SearchText) ||
               name.Contains(filter.SearchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private bool IsQuestMatch(FilterConfiguration filter, IQuestInfo questInfo)
    {
        if (!string.IsNullOrEmpty(filter.SearchText) &&
            !(questInfo.Name.Contains(filter.SearchText, StringComparison.CurrentCultureIgnoreCase) || questInfo.QuestId.ToString() == filter.SearchText))
        {
            return false;
        }

        if (filter.AvailableOnly && !questFunctions.IsReadyToAcceptQuest(questInfo.QuestId))
            return false;

        if (filter.HideNoPaths &&
            (!questRegistry.TryGetQuest(questInfo.QuestId, out Quest? quest) || quest.Root.Disabled))
            return false;

        return true;
    }

    private sealed record FilteredSection(JournalData.Section Section, List<FilteredCategory> Categories);

    private sealed record FilteredCategory(JournalData.Category Category, List<FilteredGenre> Genres);

    private sealed record FilteredGenre(JournalData.Genre Genre, List<IQuestInfo> Quests);

    private sealed record JournalCounts(int Available, int Total, int Obtainable, int Completed)
    {
        public JournalCounts()
            : this(0, 0, 0, 0)
        {
        }
    }

    internal sealed class FilterConfiguration
    {
        public bool AvailableOnly;
        public bool HideNoPaths;
        public string SearchText = string.Empty;

        public bool AdvancedFiltersActive => AvailableOnly || HideNoPaths;

        public FilterConfiguration WithoutName()
        {
            return new()
            {
                AvailableOnly = AvailableOnly,
                HideNoPaths = HideNoPaths
            };
        }
    }
}
