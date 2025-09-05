using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Questionable.Model;
using Questionable.Model.Questing;
using Microsoft.Extensions.Logging;

namespace Questionable.Data;

internal sealed class JournalData
{
    private readonly ILogger<JournalData> _logger;

    public JournalData(IDataManager dataManager, QuestData questData, ILogger<JournalData> logger)
    {
        _logger = logger;

        var genres = dataManager.GetExcelSheet<JournalGenre>()
            .Where(x => x.RowId > 0 && x.Icon > 0)
            .Select(x => new Genre(x, questData.GetAllByJournalGenre(x.RowId)))
            .ToList();

        var limsaStart = dataManager.GetExcelSheet<QuestRedo>().GetRow(1);
        var gridaniaStart = dataManager.GetExcelSheet<QuestRedo>().GetRow(2);
        var uldahStart = dataManager.GetExcelSheet<QuestRedo>().GetRow(3);
        var genreLimsa = new Genre(uint.MaxValue - 3, "Starting in Limsa Lominsa", 1,
            new uint[] { 108, 109 }.Concat(limsaStart.QuestRedoParam.Select(x => x.Quest.RowId))
                .Where(x => x != 0)
                .Select(x => questData.GetQuestInfo(QuestId.FromRowId(x)))
                .ToList());
        var genreGridania = new Genre(uint.MaxValue - 2, "Starting in Gridania", 1,
            new uint[] { 85, 123, 124 }.Concat(gridaniaStart.QuestRedoParam.Select(x => x.Quest.RowId))
                .Where(x => x != 0)
                .Select(x => questData.GetQuestInfo(QuestId.FromRowId(x)))
                .ToList());
        var genreUldah = new Genre(uint.MaxValue - 1, "Starting in Ul'dah", 1,
            new uint[] { 568, 569, 570 }.Concat(uldahStart.QuestRedoParam.Select(x => x.Quest.RowId))
                .Where(x => x != 0)
                .Select(x => questData.GetQuestInfo(QuestId.FromRowId(x)))
                .ToList());
        genres.InsertRange(0, new[] { genreLimsa, genreGridania, genreUldah });
        genres.Single(x => x.Id == 1)
            .Quests
            .RemoveAll(x =>
                genreLimsa.Quests.Contains(x) || genreGridania.Quests.Contains(x) || genreUldah.Quests.Contains(x));

        Genres = genres.ToList();
        Categories = dataManager.GetExcelSheet<JournalCategory>()
            .Where(x => x.RowId > 0)
            .Select(x => new Category(x, Genres.Where(y => y.CategoryId == x.RowId).ToList()))
        .ToList();
        Sections = dataManager.GetExcelSheet<JournalSection>()
            .Select(x => new Section(x, Categories.Where(y => y.SectionId == x.RowId).ToList()))
            .ToList();

        // Resolve the "Other Quests" section id (locale independent when possible)
        _logger.LogDebug("Resolving OtherQuests section id...");
        OtherQuestsSectionRowId = GetOtherQuestsSectionRowId(dataManager);
        _logger.LogDebug("Resolved OtherQuestsSectionRowId = {Id}", OtherQuestsSectionRowId);

        // Mark genres under the resolved section id (mutable flag on Genre)
        if (OtherQuestsSectionRowId is int otherId)
        {
            uint otherIdU = (uint)otherId;
            var otherSection = Sections.FirstOrDefault(s => s.Id == otherIdU);
            if (otherSection != null)
            {
                int marked = 0;
                foreach (var cat in otherSection.Categories)
                {
                    foreach (var g in cat.Genres)
                    {
                        g.IsUnderOtherQuests = true;
                        ++marked;
                    }
                }

                _logger.LogInformation("Marked {Count} genres as under 'Other Quests' (section id {Id})", marked, otherId);
            }
            else
            {
                _logger.LogWarning("OtherQuestsSectionRowId {Id} found but matching Section not present in constructed Sections", otherId);
            }
        }
        else
        {
            _logger.LogDebug("OtherQuestsSectionRowId not found - falling back to localized name lookup when necessary");
        }
    }

    public List<Genre> Genres { get; }
    public List<Category> Categories { get; }
    public List<Section> Sections { get; }
    public int? OtherQuestsSectionRowId { get; private set; }

    private int? GetOtherQuestsSectionRowId(IDataManager dataManager)
    {
        var sectionSheet = dataManager.GetExcelSheet<JournalSection>();
        var otherSectionRow = sectionSheet.FirstOrDefault(static s => s.Name.ToString() == "Other Quests");
        int? rowId = otherSectionRow.RowId != 0 ? (int?)otherSectionRow.RowId : null;

        // try a localized name match
        if (rowId == null)
        {
            var localized = Sections.FirstOrDefault(s => s.Name.Equals("Other Quests", System.StringComparison.OrdinalIgnoreCase));
            if (localized is not null)
                rowId = (int)localized.Id;
        }

        return rowId;
    }

    internal sealed class Genre
    {
        public Genre(JournalGenre journalGenre, List<IQuestInfo> quests)
        {
            Id = journalGenre.RowId;
            Name = journalGenre.Name.ToString();
            CategoryId = journalGenre.JournalCategory.RowId;
            Quests = quests;
            IsUnderOtherQuests = false;
        }

        public Genre(uint id, string name, uint categoryId, List<IQuestInfo> quests)
        {
            Id = id;
            Name = name;
            CategoryId = categoryId;
            Quests = quests;
            IsUnderOtherQuests = false;
        }

        public Genre(uint id, string name, uint categoryId, List<IQuestInfo> quests, bool isUnderOtherQuests = false)
        {
            Id = id;
            Name = name;
            CategoryId = categoryId;
            Quests = quests;
            IsUnderOtherQuests = isUnderOtherQuests;
        }

        public uint Id { get; }
        public string Name { get; }
        public uint CategoryId { get; }
        public List<IQuestInfo> Quests { get; }
        public bool IsUnderOtherQuests { get; set; }
    }

    internal sealed class Category(JournalCategory journalCategory, IReadOnlyList<Genre> genres)
    {
        public uint Id { get; } = journalCategory.RowId;
        public string Name { get; } = journalCategory.Name.ToString();
        public uint SectionId { get; } = journalCategory.JournalSection.RowId;
        public IReadOnlyList<Genre> Genres { get; } = genres;
    }

    internal sealed class Section(JournalSection journalSection, IReadOnlyList<Category> categories)
    {
        public uint Id { get; } = journalSection.RowId;
        public string Name { get; } = journalSection.Name.ToString();
        public IReadOnlyList<Category> Categories { get; } = categories;
    }
}
