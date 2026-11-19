using System.Globalization;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class AlliedSocietyDataTests
{
    [Theory]
    [InlineData(1379, EAlliedSociety.Sahagin, true, EAlliedSociety.Sahagin)]
    [InlineData(1389, EAlliedSociety.Sahagin, false, EAlliedSociety.None)]
    [InlineData(1257, EAlliedSociety.Sylphs, true, EAlliedSociety.None)]
    [InlineData(1569, EAlliedSociety.Ixal, true, EAlliedSociety.None)]
    [InlineData(100, EAlliedSociety.None, true, EAlliedSociety.None)]
    public void CommonTurnIn_RepeatableSocietyOnly(ushort quest, EAlliedSociety society, bool repeatable,
        EAlliedSociety expected) =>
        Assert.Equal(expected, new AlliedSocietyData().GetCommonTurnIn(TestQuestInfo.Create(quest, "Quest")
            .With(nameof(QuestInfo.AlliedSociety), society)
            .With(nameof(QuestInfo.IsRepeatable), repeatable)));

    [Fact]
    public void MountActions_HaveListedGiver()
    {
        HashSet<uint> givers = [.. new AlliedSocietyData().Mounts.Values.SelectMany(x => x.IssuerDataIds)];
        int checkedQuests = 0;
        foreach (string resource in typeof(EmbeddedQuest).Assembly.GetManifestResourceNames()
                     .Where(x => x.Contains("/Allied Societies/", StringComparison.Ordinal) &&
                                 x.EndsWith(".json", StringComparison.Ordinal)))
        {
            var embedded = new EmbeddedQuest(resource);
            if (!ushort.TryParse(embedded.ShortName.Split('_', 2)[0], NumberStyles.None,
                    CultureInfo.InvariantCulture, out ushort questId))
                continue;
            var quest = QuestTestData.LoadQuest(new QuestId(questId), embedded);
            var sequence = quest.Root.QuestSequence.FirstOrDefault(x =>
                x.Steps.Any(y => y.Action is { } action && action.RequiresMount()));
            if (sequence == null)
                continue;

            Assert.True(quest.Root.QuestSequence
                    .Where(x => x.Sequence > 0 && x.Sequence < sequence.Sequence)
                    .SelectMany(x => x.Steps)
                    .Any(x => x.DataId is { } id && givers.Contains(id)),
                $"{embedded.ShortName}: no listed mount giver before sequence {sequence.Sequence}");
            ++checkedQuests;
        }

        Assert.True(checkedQuests > 0, "No society mount quests were checked; quest resources are missing");
    }
}
