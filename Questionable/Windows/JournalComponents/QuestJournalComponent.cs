using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Validation;
using Questionable.Windows.QuestComponents;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Microsoft.Extensions.Logging;

namespace Questionable.Windows.JournalComponents;

internal sealed class QuestJournalComponent
{
    private readonly Dictionary<JournalData.Genre, JournalCounts> _genreCounts = new();
    private readonly Dictionary<JournalData.Category, JournalCounts> _categoryCounts = new();
    private readonly Dictionary<JournalData.Section, JournalCounts> _sectionCounts = new();

    private readonly JournalData _journalData;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestFunctions _questFunctions;
    private readonly UiUtils _uiUtils;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestJournalUtils _questJournalUtils;
    private readonly QuestValidator _questValidator;
    private readonly Configuration _configuration;
    private readonly ILogger<QuestJournalComponent> _logger;

    private const uint SeasonalJournalCategoryRowId = 96;

    private List<FilteredSection> _filteredSections = new();
    private bool _lastHideSeasonalGlobally;

    public QuestJournalComponent(JournalData journalData, QuestRegistry questRegistry, QuestFunctions questFunctions,
        UiUtils uiUtils, QuestTooltipComponent questTooltipComponent, IDalamudPluginInterface pluginInterface,
        QuestJournalUtils questJournalUtils, QuestValidator questValidator, Configuration configuration,
        ILogger<QuestJournalComponent> logger)
    {
        _journalData = journalData;
        _questRegistry = questRegistry;
        _questFunctions = questFunctions;
        _uiUtils = uiUtils;
        _questTooltipComponent = questTooltipComponent;
        _pluginInterface = pluginInterface;
        _questJournalUtils = questJournalUtils;
        _questValidator = questValidator;
        _configuration = configuration;
        _logger = logger;
        _lastHideSeasonalGlobally = _configuration.General.HideSeasonalEventsFromJournalProgress;
    }

    internal FilterConfiguration Filter { get; } = new();

    public void DrawQuests()
    {
        using var tab = ImRaii.TabItem("Quests");
        if (!tab)
            return;

        var currentHide = _configuration.General.HideSeasonalEventsFromJournalProgress;
        if (currentHide != _lastHideSeasonalGlobally)
        {
            _lastHideSeasonalGlobally = currentHide;
            _logger.LogDebug("Configuration change detected: HideSeasonalEventsFromJournalProgress={Hide} - refreshing journal", currentHide);
            UpdateFilter();
        }

        if (ImGui.CollapsingHeader("Explanation", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text("The list below contains all quests that appear in your journal.");
            ImGui.BulletText("'Supported' lists quests that Questionable can do for you");
            ImGui.BulletText("'Completed' lists quests your current character has completed.");
            ImGui.BulletText(
                "Not all quests can be completed even if they're listed as available, e.g. starting city quest chains or past seasonal events.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        QuestJournalUtils.ShowFilterContextMenu(this);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputTextWithHint(string.Empty, "Search quests and categories", ref Filter.SearchText, 256))
            UpdateFilter();

        if (_filteredSections.Count > 0)
        {
            using var table = ImRaii.Table("Quests", 3, ImGuiTableFlags.NoSavedSettings);
            if (!table)
                return;

            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.NoHide);
            ImGui.TableSetupColumn("Supported", ImGuiTableColumnFlags.WidthFixed, 120 * ImGui.GetIO().FontGlobalScale);
            ImGui.TableSetupColumn("Completed", ImGuiTableColumnFlags.WidthFixed, 120 * ImGui.GetIO().FontGlobalScale);
            ImGui.TableHeadersRow();

            foreach (var section in _filteredSections)
                DrawSection(section);
        }
        else
            ImGui.Text("No quest or category matches your search.");
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
            foreach (var category in filter.Categories)
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
            foreach (var genre in filter.Genres)
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

        bool open = ImGui.TreeNodeEx(filter.Genre.Name, ImGuiTreeNodeFlags.SpanFullWidth);

        ImGui.TableNextColumn();
        DrawCount(supported, total);
        ImGui.TableNextColumn();
        DrawCount(completed, obtainable);

        if (open)
        {
            foreach (var quest in filter.Quests)
                DrawQuest(quest);

            ImGui.TreePop();
        }
    }

    private void DrawQuest(IQuestInfo questInfo)
    {
        _questRegistry.TryGetQuest(questInfo.QuestId, out var quest);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TreeNodeEx($"{questInfo.Name} ({questInfo.QuestId})",
            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth);


        if (ImGui.IsItemHovered())
            _questTooltipComponent.Draw(questInfo);

        _questJournalUtils.ShowContextMenu(questInfo, quest, nameof(QuestJournalComponent));

        ImGui.TableNextColumn();
        float spacing;
        // ReSharper disable once UnusedVariable
        using (var font = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            spacing = ImGui.GetColumnWidth() / 2 - ImGui.CalcTextSize(FontAwesomeIcon.Check.ToIconString()).X;
        }

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + spacing);

        if (_questFunctions.IsQuestRemoved(questInfo.QuestId))
            _uiUtils.ChecklistItem(string.Empty, ImGuiColors.DalamudGrey, FontAwesomeIcon.Minus);
        else if (quest is { Root.Disabled: false })
        {
            List<ValidationIssue> issues = _questValidator.GetIssues(quest.Id);
            if (issues.Any(x => x.Severity == EIssueSeverity.Error))
                _uiUtils.ChecklistItem(string.Empty, ImGuiColors.DalamudRed, FontAwesomeIcon.ExclamationTriangle);
            else if (issues.Count > 0)
                _uiUtils.ChecklistItem(string.Empty, ImGuiColors.ParsedBlue, FontAwesomeIcon.InfoCircle);
            else
                _uiUtils.ChecklistItem(string.Empty, true);
        }
        else
            _uiUtils.ChecklistItem(string.Empty, false);

        ImGui.TableNextColumn();

        bool isExpired = false;
        bool isUnobtainable = _questFunctions.IsQuestUnobtainable(questInfo.QuestId);
        if (questInfo.SeasonalQuestExpiry is { } expiry)
        {
            DateTime expiryUtc = expiry.Kind == DateTimeKind.Utc ? expiry : expiry.ToUniversalTime();
            if (DateTime.UtcNow > expiryUtc)
                isExpired = true;
        }

        if (isExpired || isUnobtainable)
        {
            _uiUtils.ChecklistItem("Unobtainable", ImGuiColors.DalamudGrey, FontAwesomeIcon.Minus);
        }
        else
        {
            _uiUtils.ChecklistItem("Available", ImGuiColors.DalamudYellow, FontAwesomeIcon.Running);
        }
    }

    private static void DrawCount(int count, int total)
    {
        string len = 9999.ToString(CultureInfo.CurrentCulture);
        ImGui.PushFont(UiBuilder.MonoFont);

        if (total == 0)
            ImGui.TextColored(ImGuiColors.DalamudGrey, $"{"-".PadLeft(len.Length)} / {"-".PadLeft(len.Length)}");
        else
        {
            string text =
                $"{count.ToString(CultureInfo.CurrentCulture).PadLeft(len.Length)} / {total.ToString(CultureInfo.CurrentCulture).PadLeft(len.Length)}";
            if (count == total)
                ImGui.TextColored(ImGuiColors.ParsedGreen, text);
            else
                ImGui.TextUnformatted(text);
        }

        ImGui.PopFont();
    }

    public void UpdateFilter()
    {
        _filteredSections = _journalData.Sections
            .Select(x => FilterSection(x, Filter))
            .Where(x => x.Categories.Count > 0)
            .ToList();

        RefreshCounts();
    }

    private FilteredSection FilterSection(JournalData.Section section, FilterConfiguration filter)
    {
        IEnumerable<FilteredCategory> filteredCategories;
        var hideSeasonalGlobally = _configuration.General.HideSeasonalEventsFromJournalProgress;

        var categoriesToProcess = hideSeasonalGlobally
            ? section.Categories.Where(c => c.Id != SeasonalJournalCategoryRowId)
            : section.Categories;

        if (IsCategorySectionGenreMatch(filter, section.Name))
        {
            filteredCategories = categoriesToProcess
                .Select(x => FilterCategory(x, filter.WithoutName(), section));
        }
        else
        {
            filteredCategories = categoriesToProcess
                .Select(category => FilterCategory(category, filter, section));
        }

        return new FilteredSection(section, filteredCategories.Where(x => x.Genres.Count > 0).ToList());
    }

    private FilteredCategory FilterCategory(JournalData.Category category, FilterConfiguration filter, JournalData.Section? parentSection = null)
    {
        IEnumerable<FilteredGenre> filteredGenres;
        if (IsCategorySectionGenreMatch(filter, category.Name))
        {
            filteredGenres = category.Genres
                .Select(x => FilterGenre(x, filter.WithoutName(), parentSection));
        }
        else
        {
            filteredGenres = category.Genres
                .Select(genre => FilterGenre(genre, filter, parentSection));
        }

        return new FilteredCategory(category, filteredGenres.Where(x => x.Quests.Count > 0).ToList());
    }

    private FilteredGenre FilterGenre(JournalData.Genre genre, FilterConfiguration filter, JournalData.Section? parentSection = null)
    {
        IEnumerable<IQuestInfo> filteredQuests;
        bool hideSeasonalGlobally = _configuration.General.HideSeasonalEventsFromJournalProgress;

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

        if (hideSeasonalGlobally && genre.CategoryId == SeasonalJournalCategoryRowId)
            filteredQuests = filteredQuests.Where(q => !IsSeasonal(q));

        return new FilteredGenre(genre, filteredQuests.ToList());
    }

    internal void RefreshCounts()
    {
        _genreCounts.Clear();
        _categoryCounts.Clear();
        _sectionCounts.Clear();

        bool hideSeasonalGlobally = _configuration.General.HideSeasonalEventsFromJournalProgress;
        _logger.LogInformation("Refreshing journal counts. HideSeasonalEventsFromJournalProgress={Hide}", hideSeasonalGlobally);

        foreach (var genre in _journalData.Genres)
        {
            var relevantQuests = hideSeasonalGlobally && genre.CategoryId == SeasonalJournalCategoryRowId
                ? genre.Quests.Where(q => !IsSeasonal(q)).ToList()
                : genre.Quests.ToList();

            int available = relevantQuests.Count(x =>
                _questRegistry.TryGetQuest(x.QuestId, out var quest) &&
                !quest.Root.Disabled &&
                !_questFunctions.IsQuestRemoved(x.QuestId));
            int total = relevantQuests.Count(x => !_questFunctions.IsQuestRemoved(x.QuestId));
            int obtainable = relevantQuests.Count(x => !_questFunctions.IsQuestUnobtainable(x.QuestId));
            int completed = relevantQuests.Count(x => _questFunctions.IsQuestComplete(x.QuestId));
            _genreCounts[genre] = new(available, total, obtainable, completed);
        }

        foreach (var category in _journalData.Categories)
        {
            // If the option is enabled, skip adding the target JournalCategory row (96) entirely so it doesn't contribute to category/section totals.
            if (hideSeasonalGlobally && category.Id == SeasonalJournalCategoryRowId)
                continue;

            var counts = _genre_counts_or_default(category);
            int available = counts.Sum(x => x.Available);
            int total = counts.Sum(x => x.Total);
            int obtainable = counts.Sum(x => x.Obtainable);
            int completed = counts.Sum(x => x.Completed);
            _categoryCounts[category] = new(available, total, obtainable, completed);
        }

        foreach (var section in _journalData.Sections)
        {
            var counts = _categoryCounts
                .Where(x => section.Categories.Contains(x.Key))
                .Select(x => x.Value)
                .ToList();
            int available = counts.Sum(x => x.Available);
            int total = counts.Sum(x => x.Total);
            int obtainable = counts.Sum(x => x.Obtainable);
            int completed = counts.Sum(x => x.Completed);
            _sectionCounts[section] = new(available, total, obtainable, completed);
        }

        var grandTotal = _sectionCounts.Values.Sum(x => x.Total);
        _logger.LogDebug("RefreshCounts complete. Sections={Sections}, Categories={Categories}, Genres={Genres}, TotalQuests={Total}",
            _sectionCounts.Count, _categoryCounts.Count, _genreCounts.Count, grandTotal);
    }

    // Helper added inline to keep style consistent and avoid repeating LINQ in RefreshCounts
    private List<JournalCounts> _genre_counts_or_default(JournalData.Category category)
    {
        return _genreCounts
            .Where(x => category.Genres.Contains(x.Key))
            .Select(x => x.Value)
            .ToList();
    }

    internal void ClearCounts(int type, int code)
    {
        foreach (var genreCount in _genreCounts.ToList())
            _genreCounts[genreCount.Key] = genreCount.Value with { Completed = 0 };

        foreach (var categoryCount in _categoryCounts.ToList())
            _categoryCounts[categoryCount.Key] = categoryCount.Value with { Completed = 0 };

        foreach (var sectionCount in _sectionCounts.ToList())
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
            return false;

        if (filter.AvailableOnly && !_questFunctions.IsReadyToAcceptQuest(questInfo.QuestId))
            return false;

        if (filter.HideNoPaths &&
            (!_questRegistry.TryGetQuest(questInfo.QuestId, out var quest) || quest.Root.Disabled))
            return false;

        return true;
    }

    private static bool IsSeasonal(IQuestInfo q)
    {
        if (q == null) return false;
        if (q.IsSeasonalQuest) return true;
        if (q.SeasonalQuestExpiry is not null) return true;
        if (q is UnlockLinkQuestInfo uli && uli.QuestExpiry is not null) return true;
        return false;
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
        public string SearchText = string.Empty;
        public bool AvailableOnly;
        public bool HideNoPaths;

        public bool AdvancedFiltersActive => AvailableOnly || HideNoPaths;

        public FilterConfiguration WithoutName() => new()
        {
            AvailableOnly = AvailableOnly,
            HideNoPaths = HideNoPaths
        };
    }
}
