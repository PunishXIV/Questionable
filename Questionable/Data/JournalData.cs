using Lumina.Excel.Sheets;
using Questionable.Model.Questing;
namespace Questionable.Data;

[RegisterSingleton]
internal sealed class JournalData
{
    public JournalData(IDataManager dataManager, QuestData questData, QuestRegistry questRegistry)
    {
        List<Genre> genres = dataManager.GetExcelSheet<JournalGenre>()
            .Where(x => x.RowId > 0 && x.Icon > 0)
            .Select(x => new Genre(x, questData.GetAllByJournalGenre(x.RowId)))
            .ToList();

        QuestRedo limsaStart = dataManager.GetExcelSheet<QuestRedo>().GetRow(1);
        QuestRedo gridaniaStart = dataManager.GetExcelSheet<QuestRedo>().GetRow(2);
        QuestRedo uldahStart = dataManager.GetExcelSheet<QuestRedo>().GetRow(3);
        Genre genreLimsa = new(GenreStartingInLimsa, _L("Starting in Limsa Lominsa"), 1,
            new uint[] { 108, 109 }.Concat(limsaStart.QuestRedoParam.Select(x => x.Quest.RowId))
                .Where(x => x != 0)
                .Select(x => questData.GetQuestInfo(QuestId.FromRowId(x)))
                .ToList());
        Genre genreGridania = new(GenreStartingInGridania, _L("Starting in Gridania"), 1,
            new uint[] { 85, 123, 124 }.Concat(gridaniaStart.QuestRedoParam.Select(x => x.Quest.RowId))
                .Where(x => x != 0)
                .Select(x => questData.GetQuestInfo(QuestId.FromRowId(x)))
                .ToList());
        Genre genreUldah = new(GenreStartingInUldah, _L("Starting in Ul'dah"), 1,
            new uint[] { 568, 569, 570 }.Concat(uldahStart.QuestRedoParam.Select(x => x.Quest.RowId))
                .Where(x => x != 0)
                .Select(x => questData.GetQuestInfo(QuestId.FromRowId(x)))
                .ToList());
        genres.InsertRange(0, [genreLimsa, genreGridania, genreUldah]);
        genres.Single(x => x.Id == 1)
            .Quests
            .RemoveAll(x =>
                genreLimsa.Quests.Contains(x) || genreGridania.Quests.Contains(x) || genreUldah.Quests.Contains(x));

        Genre instantGenre = new(GenreInstantQuests, _L("Instant Quests"), CategorySpecialQuests,
            questRegistry.AllQuests
                     .Where(x => x.Info is QuestInfo qInfo && qInfo.CompletesInstantly)
                     .Select(x => x.Info)
                     .OrderBy(x => x.QuestId.Value)
                     .ToList()
        );
        genres = genres.Append(instantGenre).ToList();

        Genre collabQuests = genres.Where(g => g.Id.Equals(GenreCollaborationQuests)).Single();
        collabQuests.Quests.Add(((ushort[])[
            1153, 1154, 1155, 1556, // ffxiii 2013
            1287, // ffxi 2014
            1288, // dqx
            2141, // yokai
            2206, // ffxi 2015
            3158, 3159, 3160, // ffxv
            4796, 4797, 4798, // ffxvi
            4801, // fall guys
        ]).FromNumericListOfQuests().Select(q => questData.GetQuestInfo(q)).ToArray());

        // Reassign special sidequests to another genre
        Dictionary<QuestId, uint> questReassignPairs = new() {
            { new(432), GenreSpecialQuests }, // arr ex mount
            { new(1550), GenreSpecialQuests }, // hw ex mount
            { new(3200), GenreSpecialQuests }, // stb ex mount
            { new(4057), GenreSpecialQuests }, // shb ex mount
            { new(4795), GenreSpecialQuests }, // ew ex mount
            { new(5469), GenreSpecialQuests }, // dt ex mount
        };
        //foreach (var genre in genres)
        //{
        //    foreach (var q in genre.Quests)
        //    {
        //        if (q.Level == 1)
        //            questReassignPairs[(QuestId)q.QuestId] = GenreSpecialQuests;
        //    }
        //}
        foreach (var kvp in questReassignPairs)
        {
            var info = questData.GetQuestInfo(kvp.Key);
            foreach (var _g in genres)
                _g.Quests.Remove(info);
            Genre genreAdd = genres.Where(g => g.Id.Equals(kvp.Value)).Single();
            genreAdd.Quests.Add(questData.GetQuestInfo(kvp.Key));
        }

        Genres = genres.ToList();
        Categories = dataManager.GetExcelSheet<JournalCategory>()
            .Where(x => x.RowId > 0)
            .Select(x => new Category(x, Genres.Where(y => y.CategoryId == x.RowId).ToList()))
            .ToList();
        Sections = dataManager.GetExcelSheet<JournalSection>()
            .Select(x => new Section(x, Categories.Where(y => y.SectionId == x.RowId).ToList()))
            .ToList();
    }
    public const uint CategorySpecialQuests = 98;
    public const uint GenreSpecialQuests = 251;
    public const uint GenreCollaborationQuests = 252;
    public const uint GenreStartingInUldah = uint.MaxValue - 1;
    public const uint GenreStartingInGridania = uint.MaxValue - 2;
    public const uint GenreStartingInLimsa = uint.MaxValue - 3;
    public const uint GenreInstantQuests = uint.MaxValue - 4;

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
