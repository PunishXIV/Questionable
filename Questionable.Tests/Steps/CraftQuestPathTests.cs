using System.Globalization;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CraftQuestPathTests
{
    [Fact]
    public void ShippedListOnlyCrafts_HaveVerifiableOutputsAtTheirActualSequence()
    {
        int checkedSteps = 0;
        foreach (string resource in typeof(EmbeddedQuest).Assembly.GetManifestResourceNames()
                     .Where(x => x.StartsWith("QuestPaths/", StringComparison.Ordinal) &&
                                 x.EndsWith(".json", StringComparison.Ordinal)))
        {
            var embedded = new EmbeddedQuest(resource);
            string prefix = embedded.ShortName.Split('_', 2)[0];
            if (!ushort.TryParse(prefix, NumberStyles.None, CultureInfo.InvariantCulture, out ushort questId))
                continue;

            var quest = QuestTestData.LoadQuest(new QuestId(questId), embedded);
            foreach (var sequence in quest.Root.QuestSequence)
            {
                foreach (var step in sequence.Steps.Where(x => x.InteractionType == EInteractionType.Craft &&
                             x.ItemId == null && x.ItemCount == null))
                {
                    var targets = Craft.GetItemTargets(new Craft.CraftTask(quest, sequence.Sequence, step));
                    Assert.True(targets.Count > 0, $"{embedded.ShortName}, sequence {sequence.Sequence} has no craft outputs");
                    Assert.False(Craft.AreItemsSatisfied(targets, _ => 0));
                    Assert.True(Craft.AreItemsSatisfied(targets, item => item.ItemCount));
                    ++checkedSteps;
                }
            }
        }

        Assert.True(checkedSteps > 0, "No list-only craft steps were checked; quest resources are missing");
    }
}
