using System.Collections.Immutable;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class JournalDataTests
{
    private static AetherCurrentZoneInfo Zone(ushort territory, EExpansionVersion expansion) =>
        new(new AetherCurrentId(territory), $"Zone {territory}", expansion, [1u, 2u], ImmutableList<PreviousQuestInfo>.Empty);

    private static JournalData.Section Other(params IQuestInfo[] entries) =>
        JournalData.CreateOtherSection(entries, "Aether Currents", x => x.ToString());

    [Fact]
    public void OtherSection_GroupsZonesByExpansion()
    {
        JournalData.Section section = Other(
            Zone(813, EExpansionVersion.Shadowbringers), Zone(398, EExpansionVersion.Heavensward),
            Zone(397, EExpansionVersion.Heavensward), TestQuestInfo.Create(1000));

        JournalData.Category currents = section.Categories.Single(x => x.Id == JournalData.CategoryAetherCurrents);
        Assert.Equal(["Heavensward", "Shadowbringers"], currents.Genres.Select(x => x.Name));
        Assert.Equal([397, 398], currents.Genres[0].Quests.Select(x => (int)x.QuestId.Value));
        Assert.All(currents.Genres, x => Assert.Equal(JournalData.CategoryAetherCurrents, x.CategoryId));
    }

    [Fact]
    public void OtherSection_HoldsFantasiasAndNoQuests()
    {
        JournalData.Section section = Other(
            new UnlockLinkQuestInfo(new UnlockLinkId(506), "Fantasia 1", 1), TestQuestInfo.Create(1000),
            Zone(397, EExpansionVersion.Heavensward));

        JournalData.Category fantasia = section.Categories.Single(x => x.Id == JournalData.CategoryFantasia);
        Assert.Equal([new UnlockLinkId(506)], fantasia.Genres.Single().Quests.Select(x => x.QuestId));
        Assert.DoesNotContain(section.Categories.SelectMany(x => x.Genres).SelectMany(x => x.Quests),
            x => x.QuestId is QuestId);
    }

    [Fact]
    public void OtherSection_IdsDontClashWithGameGenres()
    {
        JournalData.Section section = Other(Zone(397, EExpansionVersion.Heavensward),
            new UnlockLinkQuestInfo(new UnlockLinkId(506), "Fantasia 1", 1));

        uint[] genreIds = [.. section.Categories.SelectMany(x => x.Genres).Select(x => x.Id)];
        uint[] builtIn = [JournalData.GenreStartingInLimsa, JournalData.GenreStartingInGridania,
            JournalData.GenreStartingInUldah, JournalData.GenreInstantQuests];
        Assert.Empty(genreIds.Intersect(builtIn));
        Assert.Equal(genreIds.Length, genreIds.Distinct().Count());
    }
}
