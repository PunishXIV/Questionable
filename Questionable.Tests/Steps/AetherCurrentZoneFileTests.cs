using System.Globalization;
using Questionable.Data;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class AetherCurrentZoneFileTests
{
    private static IEnumerable<EmbeddedQuest> Resources() =>
        typeof(EmbeddedQuest).Assembly.GetManifestResourceNames()
            .Where(x => x.StartsWith("QuestPaths/", StringComparison.Ordinal) && x.EndsWith(".json", StringComparison.Ordinal))
            .Select(x => new EmbeddedQuest(x));

    [Fact]
    public void ZoneFiles_CoverEveryAttuneStepInPaths()
    {
        Dictionary<uint, SortedSet<uint>> inQuests = [];
        Dictionary<ushort, QuestStep[]> zones = [];
        foreach (EmbeddedQuest embedded in Resources())
        {
            string prefix = embedded.ShortName.Split('_', 2)[0];
            if (prefix.StartsWith('C'))
            {
                ushort zone = ushort.Parse(prefix[1..], CultureInfo.InvariantCulture);
                zones[zone] = [.. QuestTestData.LoadQuest(new AetherCurrentId(zone), embedded).Root.QuestSequence
                    .Single().Steps];
                continue;
            }

            if (!ushort.TryParse(prefix, NumberStyles.None, CultureInfo.InvariantCulture, out ushort questId))
                continue;

            foreach (QuestStep step in QuestTestData.LoadQuest(new QuestId(questId), embedded).Root.QuestSequence
                         .SelectMany(x => x.Steps)
                         .Where(x => x is { InteractionType: EInteractionType.AttuneAetherCurrent, AetherCurrentId: not null }))
            {
                if (!inQuests.TryGetValue(step.TerritoryId, out SortedSet<uint>? currents))
                    inQuests[step.TerritoryId] = currents = [];
                currents.Add(step.AetherCurrentId!.Value);
            }
        }

        Assert.Equal(QuestData.AetherCurrentZoneQuests.Keys.Order(), zones.Keys.Order());
        foreach ((ushort zone, QuestStep[] steps) in zones)
        {
            QuestStep[] attunes = [.. steps.Where(x => x.InteractionType == EInteractionType.AttuneAetherCurrent)];
            Assert.Equal(inQuests[zone], attunes.Select(x => x.AetherCurrentId!.Value).Order());
            Assert.All(steps, x => Assert.Equal(zone, x.TerritoryId));
            Assert.NotNull(steps[0].AetheryteShortcut);
        }
    }

    [Fact]
    public void ZoneWaypoints_SkipOnTheirOwnCurrent()
    {
        int waypoints = 0;
        foreach (EmbeddedQuest embedded in Resources().Where(x => x.ShortName.StartsWith('C')))
        {
            ushort zone = ushort.Parse(embedded.ShortName[1..embedded.ShortName.IndexOf('_')], CultureInfo.InvariantCulture);
            List<QuestStep> steps = QuestTestData.LoadQuest(new AetherCurrentId(zone), embedded).Root.QuestSequence.Single().Steps;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].InteractionType == EInteractionType.AttuneAetherCurrent)
                    continue;

                uint? current = steps[i].SkipConditions?.StepIf?.AetherCurrentUnlocked;
                QuestStep next = steps.Skip(i + 1).First(x => x.InteractionType == EInteractionType.AttuneAetherCurrent);
                Assert.Equal(next.AetherCurrentId, current);
                ++waypoints;
            }
        }

        Assert.True(waypoints > 0);
    }
}
