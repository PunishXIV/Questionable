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

        Genre collabGenre = genres.Where(g => g.Id.Equals(GenreCollaborationQuests)).Single();
        collabGenre.Quests.Add(((ushort[])[
            1153, 1154, 1155, 1156, // ffxiii 2013
            1287, // ffxi 2014
            1288, // dqx
            2141, // yokai
            2206, // ffxi 2015
            3158, 3159, 3160, // ffxv
            4796, 4797, 4798, // ffxvi
            4801, // fall guys
        ]).FromNumericListOfQuests().Select(q => questData.GetQuestInfo(q)).ToArray());

        Dictionary<QuestId, uint> reassignments = new() {
            { new(432), GenreSpecialQuests }, // arr ex mount
            { new(1550), GenreSpecialQuests }, // hw ex mount
            { new(3200), GenreSpecialQuests }, // stb ex mount
            { new(4057), GenreSpecialQuests }, // shb ex mount
            { new(4795), GenreSpecialQuests }, // ew ex mount
            { new(5469), GenreSpecialQuests }, // dt ex mount
        };
        foreach (var kvp in reassignments)
        {
            var info = questData.GetQuestInfo(kvp.Key);
            foreach (var genre in genres)
                genre.Quests.Remove(info);
            Genre target = genres.Where(g => g.Id.Equals(kvp.Value)).Single();
            target.Quests.Add(questData.GetQuestInfo(kvp.Key));
        }

        Section other = CreateOtherSection(questData.GetAll(), _T<Addon>(2445), x => _T<ExVersion>((uint)x));
        Genres = [.. genres, .. other.Categories.SelectMany(x => x.Genres)];
        Categories = dataManager.GetExcelSheet<JournalCategory>()
            .Where(x => x.RowId > 0)
            .Select(x => new Category(x, genres.Where(y => y.CategoryId == x.RowId).ToList()))
            .Concat(other.Categories)
            .ToList();
        Sections = dataManager.GetExcelSheet<JournalSection>()
            .Select(x => new Section(x, Categories.Where(y => y.SectionId == x.RowId).ToList()))
            .Append(other)
            .ToList();
    }

    // QST's own list entries have no place in the game's journal, so they get a last section of their own.
    internal static Section CreateOtherSection(IEnumerable<IQuestInfo> entries, string aetherCurrents,
        Func<EExpansionVersion, string> expansionName)
    {
        List<IQuestInfo> all = [.. entries];
        List<Genre> currentGenres = [.. all.OfType<AetherCurrentZoneInfo>()
            .GroupBy(x => x.Expansion)
            .OrderBy(x => x.Key)
            .Select(x => new Genre(GenreAetherCurrents - (uint)x.Key, expansionName(x.Key), CategoryAetherCurrents,
                [.. x.OrderBy(y => y.SortKey)]))];
        Genre fantasia = new(GenreFantasia, _L("Fantasia"), CategoryFantasia, [.. all.OfType<UnlockLinkQuestInfo>()]);
        return new Section(SectionOther, _L("Other"),
        [
            new Category(CategoryAetherCurrents, aetherCurrents, SectionOther, currentGenres),
            new Category(CategoryFantasia, _L("Fantasia"), SectionOther, [fantasia]),
        ]);
    }
    public const uint CategorySpecialQuests = 98;
    public const uint GenreSpecialQuests = 251;
    public const uint GenreCollaborationQuests = 252;
    public const uint GenreStartingInUldah = uint.MaxValue - 1;
    public const uint GenreStartingInGridania = uint.MaxValue - 2;
    public const uint GenreStartingInLimsa = uint.MaxValue - 3;
    public const uint GenreInstantQuests = uint.MaxValue - 4;
    public const uint GenreFantasia = uint.MaxValue - 5;
    public const uint GenreAetherCurrents = uint.MaxValue - 20;
    public const uint CategoryAetherCurrents = uint.MaxValue - 1;
    public const uint CategoryFantasia = uint.MaxValue - 2;
    public const uint SectionOther = uint.MaxValue - 1;

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

    internal sealed class Category(uint id, string name, uint sectionId, IReadOnlyList<Genre> genres)
    {
        public Category(JournalCategory journalCategory, IReadOnlyList<Genre> genres)
            : this(journalCategory.RowId, journalCategory.Name.ToString(), journalCategory.JournalSection.RowId, genres)
        {
        }

        public uint Id { get; } = id;
        public string Name { get; } = name;
        public uint SectionId { get; } = sectionId;
        public IReadOnlyList<Genre> Genres { get; } = genres;
    }

    internal sealed class Section(uint id, string name, IReadOnlyList<Category> categories)
    {
        public Section(JournalSection journalSection, IReadOnlyList<Category> categories)
            : this(journalSection.RowId, journalSection.Name.ToString(), categories)
        {
        }

        public uint Id { get; } = id;
        public string Name { get; } = name;
        public IReadOnlyList<Category> Categories { get; } = categories;
    }
}
