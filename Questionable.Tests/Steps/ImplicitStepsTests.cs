using System.Globalization;
using System.Numerics;
using Lumina.Excel.Sheets;
using Questionable.Domain;
using Questionable.Model.Questing;
using Questionable.Tests.AutoGen;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class ImplicitStepsTests
{
    private static readonly Vector3 Here = new(1, 2, 3);

    [Fact]
    public void EmptyAccept_UsesQuestGiver()
    {
        QuestRoot root = Root(0);

        Assert.True(ImplicitSteps.Fill(root, 100, 200, Here, 132));

        QuestStep step = Assert.Single(root.QuestSequence[0].Steps);
        Assert.Equal((EInteractionType.AcceptQuest, 100u, Here, 132u),
            (step.InteractionType, step.DataId!.Value, step.Position!.Value, step.TerritoryId));
    }

    [Fact]
    public void EmptyTurnIn_OnlyBackToGiver()
    {
        QuestRoot back = Root(255);
        QuestRoot elsewhere = Root(255);

        Assert.True(ImplicitSteps.Fill(back, 100, 100, Here, 132));
        Assert.False(ImplicitSteps.Fill(elsewhere, 100, 200, Here, 132));

        Assert.Equal(EInteractionType.CompleteQuest, Assert.Single(back.QuestSequence[0].Steps).InteractionType);
        Assert.Empty(elsewhere.QuestSequence[0].Steps);
    }

    [Fact]
    public void MissingOrFilledSequences_Untouched()
    {
        QuestRoot root = new() { QuestSequence = [new() { Sequence = 1, Steps = [new QuestStep()] }] };

        Assert.True(ImplicitSteps.Fill(root, 100, 100, Here, 132));

        Assert.Single(root.QuestSequence);
        Assert.Single(root.QuestSequence[0].Steps);
    }

    [Fact]
    public void NoQuestGiver_ReportsUnfilled()
    {
        Assert.False(ImplicitSteps.Fill(Root(0), 0, 0, Here, 132));
    }

    [GameDataFact]
    public void ShippedEmptySequences_AreFillable()
    {
        using var gameData = new Lumina.GameData(GameDataFixture.FindInstalledSqPackDirectory()
                                                 ?? Environment.GetEnvironmentVariable(GameDataFixture.GamePathEnvVar)!);
        var quests = gameData.GetExcelSheet<Lumina.Excel.Sheets.Quest>()!;
        var levels = gameData.GetExcelSheet<Level>()!;
        int checkedSequences = 0;
        foreach (string resource in typeof(EmbeddedQuest).Assembly.GetManifestResourceNames()
                     .Where(x => x.StartsWith("QuestPaths/", StringComparison.Ordinal) && x.EndsWith(".json", StringComparison.Ordinal)))
        {
            var embedded = new EmbeddedQuest(resource);
            if (!ushort.TryParse(embedded.ShortName.Split('_', 2)[0], NumberStyles.None, CultureInfo.InvariantCulture, out ushort id))
                continue;
            var root = QuestTestData.LoadQuest(new QuestId(id), embedded).Root;
            if (!root.QuestSequence.Any(x => x.Steps.Count == 0))
                continue;

            var quest = quests.GetRow(65536u + id);
            var level = levels.GetRowOrDefault(quest.IssuerLocation.RowId);
            Assert.True(level != null, $"{embedded.ShortName}: no issuer location");
            Assert.True(ImplicitSteps.Fill(root, quest.IssuerStart.RowId, quest.TargetEnd.RowId,
                    new(level.Value.X, level.Value.Y, level.Value.Z), level.Value.Territory.RowId),
                $"{embedded.ShortName}: game data can't fill its empty sequence");
            checkedSequences++;
        }

        Assert.True(checkedSequences > 3000, string.Create(CultureInfo.InvariantCulture, $"Only {checkedSequences} paths with empty sequences were checked"));
    }

    private static QuestRoot Root(byte sequence) => new() { QuestSequence = [new() { Sequence = sequence }] };
}
