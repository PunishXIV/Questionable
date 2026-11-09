using System.Diagnostics.CodeAnalysis;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows.JournalComponents;

[RegisterSingleton]
internal sealed class QuestJournalComponent
(
    JournalData journalData,
    QuestRegistry questRegistry,
    QuestFunctions questFunctions,
    QuestJournalUtils questJournalUtils,
    JournalTable journalTable,
    RedoUtil redoUtil,
    Configuration configuration,
    IDalamudPluginInterface pluginInterface)
{
    private readonly Dictionary<JournalData.Category, Counts> _categoryCounts = [];
    private readonly Dictionary<JournalData.Genre, Counts> _genreCounts = [];
    private readonly Dictionary<JournalData.Section, Counts> _sectionCounts = [];

    private readonly JournalRefresh _refresh = new(TimeSpan.FromSeconds(2));
    private List<FilteredSection> _filteredSections = [];

    internal FilterOptions Filter { get; } = FilterOptions.From(configuration.Journal);

    private bool _allOpen;
    private bool _applyOpenState = true;
    private int _resultCount;

    public void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Quests"));
        if (!tab)
            return;

        if (_refresh.Tick(DateTime.Now, RecountChanged))
            ApplyFilter();

        if (QstWidgets.SectionHeader(_L("Explanation"), "JournalExplanation", defaultOpen: false))
        {
            ImGui.Text(_L("The list below contains all quests that appear in your journal."));
            ImGui.BulletText(_L("'Supported' lists quests that Questionable can do for you"));
            ImGui.BulletText(_L("'Completed' lists quests your current character has completed."));
            ImGui.BulletText(
                _L("Not all quests can be completed even if they're listed as available, e.g. starting city quest chains."));
            ImGui.BulletText(_L("The text in the Supported column indicates the last time a quest path was reported to work perfectly."));
            ImGui.TextColoredWrapped(QstTheme.Amber, _L("Quests can be added to Priority Quests, either individually or by group, with the right click menu."));

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        QuestJournalUtils.ShowFilterContextMenu(this);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetStyle().ItemSpacing.X);
        using (ImRaii.Disabled(Filter.SearchText.Length == 0))
        {
            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Times))
            {
                Filter.SearchText = string.Empty;
                UpdateFilter();
                _allOpen = false;
                _applyOpenState = true;
            }
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("9999").X - ImGui.GetStyle().ItemSpacing.X);
        if (ImGui.InputTextWithHint(string.Empty, _L("Search quests and categories"), ref Filter.SearchText, 256))
        {
            var openAll = Filter.SearchText.Length > 1;
            if (openAll != _allOpen)
            {
                _allOpen = openAll;
                _applyOpenState = true;
            }
            UpdateFilter();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.CalcTextSize("9999").X);
        ImGui.TextUnformatted($"{(_resultCount != 0 ? _resultCount : "-")}");
        _resultCount = 0;

        if (_filteredSections.Count > 0)
        {
            using ImRaii.TableDisposable table = journalTable.Begin("Quests");
            if (!table)
                return;

            foreach (FilteredSection section in _filteredSections)
                DrawSection(section);
        }
        else
            ImGui.Text(_L("No quest or category matches your search."));

        _applyOpenState = false;
    }

    private void DrawSection(FilteredSection section)
    {
        (int available, int total, int obtainable, int completed) =
            _sectionCounts.GetValueOrDefault(section.Section, new());
        if (total == 0)
            return;

        JournalTable.BeginRow();

        if (_applyOpenState)
            ImGui.SetNextItemOpen(_allOpen, ImGuiCond.Always);
        bool open = ImGui.TreeNodeEx(section.Section.Name, ImGuiTreeNodeFlags.SpanFullWidth);

        journalTable.Counts(available, total, completed, obtainable);

        if (open)
        {
            foreach (FilteredCategory category in section.Categories)
                DrawCategory(category);

            ImGui.TreePop();
        }
    }

    private void DrawCategory(FilteredCategory category)
    {
        (int available, int total, int obtainable, int completed) =
            _categoryCounts.GetValueOrDefault(category.Category, new());
        if (total == 0)
            return;

        JournalTable.BeginRow();

        if (_applyOpenState)
            ImGui.SetNextItemOpen(_allOpen, ImGuiCond.Always);
        bool open = ImGui.TreeNodeEx(category.Category.Name, ImGuiTreeNodeFlags.SpanFullWidth);

        journalTable.Counts(available, total, completed, obtainable);

        if (open)
        {
            foreach (FilteredGenre genre in category.Genres)
                DrawGenre(genre);

            ImGui.TreePop();
        }
    }

    private void DrawGenre(FilteredGenre genre)
    {
        (int supported, int total, int obtainable, int completed) = _genreCounts.GetValueOrDefault(genre.Genre, new());
        if (total == 0)
            return;

        JournalTable.BeginRow();

        string genreName = genre.Genre.Name;
        if (genre.Genre.Id != JournalData.GenreInstantQuests && genre.Quests[0].QuestId is QuestId &&
            questRegistry.TryGetQuest(genre.Quests[0].QuestId, out Quest? firstQuest))
        {
            RedoIndex redoIndex = redoUtil.GetChapter(firstQuest.Id.Value);
            if (redoIndex.Index != -1)
                genreName = $"{genre.Genre.Name} ({redoIndex.Chapter.ChapterName})";
        }

        if (_applyOpenState)
            ImGui.SetNextItemOpen(_allOpen, ImGuiCond.Always);
        bool open = ImGui.TreeNodeEx(genreName, ImGuiTreeNodeFlags.SpanFullWidth);

        questJournalUtils.ShowGroupContextMenu($"DrawGenre{genre.Genre.Id}", genre.Quests);

        journalTable.Counts(supported, total, completed, obtainable);

        if (open)
        {
            foreach (IQuestInfo quest in genre.Quests)
            {
                journalTable.QuestRow(quest, nameof(QuestJournalComponent));
                _resultCount++;
            }

            ImGui.TreePop();
        }
    }

    internal void SaveFilter()
    {
        Filter.SaveTo(configuration.Journal);
        pluginInterface.SavePluginConfig(configuration);
    }

    public void UpdateFilter()
    {
        ApplyFilter();
        RefreshCounts();
    }

    private void ApplyFilter() =>
        _filteredSections = journalData.Sections
            .Select(x => FilterSection(x, Filter))
            .Where(x => x.Categories.Count > 0)
            .ToList();

    private bool RecountChanged()
    {
        Dictionary<JournalData.Genre, Counts> before = new(_genreCounts);
        RefreshCounts();
        return before.Count != _genreCounts.Count ||
               _genreCounts.Any(x => !before.TryGetValue(x.Key, out Counts? old) || old != x.Value);
    }

    private FilteredSection FilterSection(JournalData.Section section, FilterOptions filter)
    {
        IEnumerable<FilteredCategory> filteredCategories;
        if (IsNameMatch(filter, section.Name))
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

    private FilteredCategory FilterCategory(JournalData.Category category, FilterOptions filter)
    {
        IEnumerable<FilteredGenre> filteredGenres;
        if (IsNameMatch(filter, category.Name))
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

    private FilteredGenre FilterGenre(JournalData.Genre genre, FilterOptions filter)
    {
        IEnumerable<IQuestInfo> filteredQuests;
        if (IsNameMatch(filter, genre.Name))
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
            List<Counts> counts = _genreCounts
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
            List<Counts> counts = _categoryCounts
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
        foreach (KeyValuePair<JournalData.Genre, Counts> genreCount in _genreCounts.ToList())
            _genreCounts[genreCount.Key] = genreCount.Value with { Completed = 0 };

        foreach (KeyValuePair<JournalData.Category, Counts> categoryCount in _categoryCounts.ToList())
            _categoryCounts[categoryCount.Key] = categoryCount.Value with { Completed = 0 };

        foreach (KeyValuePair<JournalData.Section, Counts> sectionCount in _sectionCounts.ToList())
            _sectionCounts[sectionCount.Key] = sectionCount.Value with { Completed = 0 };
    }

    private static bool IsNameMatch(FilterOptions filter, string name)
    {
        return string.IsNullOrEmpty(filter.SearchText) ||
               name.Contains(filter.SearchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private bool IsQuestMatch(FilterOptions filter, IQuestInfo questInfo)
    {
        if (!string.IsNullOrEmpty(filter.SearchText) &&
            !(questInfo.Name.Contains(filter.SearchText, StringComparison.CurrentCultureIgnoreCase) || questInfo.QuestId.ToString() == filter.SearchText))
        {
            return false;
        }

        if (filter.AvailableOnly && !questFunctions.IsReadyToAcceptQuest(questInfo.QuestId))
            return false;

        if (filter.BlueOnly && (questInfo is not QuestInfo info ||
                                (info.AvailableIcon is uint icon && !Enumerable.Range(40, 10).Contains((int)icon % 100))))
            return false;

        if (filter.HideNoPaths &&
            (!questRegistry.TryGetQuest(questInfo.QuestId, out Quest? quest) || quest.Root.Disabled))
            return false;

        if (filter.HideCompleted && questFunctions.IsQuestComplete(questInfo.QuestId))
            return false;

        if (filter.HideUnobtainable && questFunctions.IsQuestUnobtainable(questInfo.QuestId))
            return false;

        if (filter.HideRepeatable && questInfo.IsRepeatable)
            return false;
        return true;
    }

    private sealed record FilteredSection(JournalData.Section Section, List<FilteredCategory> Categories);

    private sealed record FilteredCategory(JournalData.Category Category, List<FilteredGenre> Genres);

    private sealed record FilteredGenre(JournalData.Genre Genre, List<IQuestInfo> Quests);

    private sealed record Counts(int Available, int Total, int Obtainable, int Completed)
    {
        public Counts()
            : this(0, 0, 0, 0)
        {
        }
    }

    internal sealed class FilterOptions
    {
        public bool AvailableOnly;
        public bool BlueOnly;
        public bool HideNoPaths;
        public bool HideCompleted;
        public bool HideUnobtainable;
        public bool HideRepeatable;
        public string SearchText = string.Empty;

        // The search text isn't saved, so the journal reopens unfiltered by name.
        public static FilterOptions From(Configuration.JournalConfiguration saved) => new()
        {
            AvailableOnly = saved.AvailableOnly,
            BlueOnly = saved.BlueOnly,
            HideNoPaths = saved.HideNoPaths,
            HideCompleted = saved.HideCompleted,
            HideUnobtainable = saved.HideUnobtainable,
            HideRepeatable = saved.HideRepeatable,
        };

        public void SaveTo(Configuration.JournalConfiguration saved)
        {
            saved.AvailableOnly = AvailableOnly;
            saved.BlueOnly = BlueOnly;
            saved.HideNoPaths = HideNoPaths;
            saved.HideCompleted = HideCompleted;
            saved.HideUnobtainable = HideUnobtainable;
            saved.HideRepeatable = HideRepeatable;
        }

        public FilterOptions WithoutName()
        {
            return new()
            {
                AvailableOnly = AvailableOnly,
                BlueOnly = BlueOnly,
                HideNoPaths = HideNoPaths,
                HideCompleted = HideCompleted,
                HideUnobtainable = HideUnobtainable,
                HideRepeatable = HideRepeatable
            };
        }
    }
}
