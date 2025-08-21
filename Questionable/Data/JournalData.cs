using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Data;

internal sealed class JournalData
{
    public JournalData(IDataManager dataManager, QuestData questData)
    {
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
        genres.InsertRange(0, [genreLimsa, genreGridania, genreUldah]);
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
    }

    public List<Genre> Genres { get; }
    public List<Category> Categories { get; }
    public List<Section> Sections { get; }

    internal sealed class Genre
    {
        public Genre(JournalGenre journalGenre, List<IQuestInfo> quests)
        {
            Id = journalGenre.RowId;
            Name = journalGenre.Name.ToString();
            CategoryId = journalGenre.JournalCategory.RowId;
            Quests = quests;
        }

        public Genre(uint id, string name, uint categoryId, List<IQuestInfo> quests)
        {
            Id = id;
            Name = name;
            CategoryId = categoryId;
            Quests = quests;
        }

        public uint Id { get; }
        public string Name { get; }
        public uint CategoryId { get; }
        public List<IQuestInfo> Quests { get; }
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
