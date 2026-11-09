using Newtonsoft.Json;
using Questionable.Windows.JournalComponents;
using Xunit;

namespace Questionable.Tests.Serialization;

public sealed class JournalFilterTests
{
    [Fact]
    public void SavedFilters_SurviveReload()
    {
        Configuration configuration = new();
        new QuestJournalComponent.FilterOptions
        {
            AvailableOnly = true,
            HideNoPaths = true,
            HideCompleted = true,
            HideRepeatable = true,
            SearchText = "not saved",
        }.SaveTo(configuration.Journal);

        Configuration loaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(configuration))!;
        QuestJournalComponent.FilterOptions filter = QuestJournalComponent.FilterOptions.From(loaded.Journal);

        Assert.True(filter.AvailableOnly);
        Assert.False(filter.BlueOnly);
        Assert.True(filter.HideNoPaths);
        Assert.True(filter.HideCompleted);
        Assert.False(filter.HideUnobtainable);
        Assert.True(filter.HideRepeatable);
        Assert.Empty(filter.SearchText);
    }

    [Fact]
    public void OldConfig_StartsWithNoFilters()
    {
        Configuration loaded = JsonConvert.DeserializeObject<Configuration>("{}")!;
        QuestJournalComponent.FilterOptions filter = QuestJournalComponent.FilterOptions.From(loaded.Journal);

        Assert.False(filter.AvailableOnly || filter.BlueOnly || filter.HideNoPaths || filter.HideCompleted ||
                     filter.HideUnobtainable || filter.HideRepeatable);
    }
}
