using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Questionable.Utils;
using Xunit;

namespace Questionable.Tests.Serialization;

public sealed class QuestPathSaveStabilityTests
{
    [Fact]
    public void EmptyFlagLists_NotWritten()
    {
        string json = JsonSerializer.Serialize(new SkipStepConditions(), JsonOptions.Default);

        Assert.DoesNotContain("CompletionQuestVariablesFlags", json, StringComparison.Ordinal);
    }

    [Theory]
    [ClassData(typeof(EmbeddedQuestLoader))]
    public void SavingAQuestPathProducesIdenticalJson(EmbeddedQuest embedded)
    {
        QuestRoot original;
        using (var stream = embedded.OpenStream())
        {
            original = JsonSerializer.Deserialize<QuestRoot>(stream)
                       ?? throw new Xunit.Sdk.XunitException(
                           $"'{embedded.ManifestName}' deserialized to a null QuestRoot.");
        }

        string once = JsonSerializer.Serialize(original, JsonOptions.Default);
        QuestRoot reloaded = JsonSerializer.Deserialize<QuestRoot>(once)
                             ?? throw new Xunit.Sdk.XunitException(
                                 $"Re-deserializing '{embedded.ManifestName}' returned null.");
        string twice = JsonSerializer.Serialize(reloaded, JsonOptions.Default);

        Assert.Equal(once, twice);
    }

    [Theory]
    [ClassData(typeof(EmbeddedQuestLoader))]
    public void SavedFile_LoadsBackTheSame(EmbeddedQuest embedded)
    {
        QuestRoot original;
        using (var stream = embedded.OpenStream())
            original = JsonSerializer.Deserialize<QuestRoot>(stream)!;

        string file = QuestPathJson.Write((JsonObject)JsonSerializer.SerializeToNode(original, JsonOptions.Default)!,
            JsonOptions.Default);
        QuestRoot reloaded = JsonSerializer.Deserialize<QuestRoot>(file)!;

        Assert.Equal(JsonSerializer.Serialize(original, JsonOptions.Default),
            JsonSerializer.Serialize(reloaded, JsonOptions.Default));
    }

    [Fact]
    public void OldAndNewLayout_BothLoad()
    {
        QuestSequence sequence = JsonSerializer.Deserialize<QuestSequence>("""
            {"Sequence": 1, "TerritoryId": 5, "Steps": [
              {"Position": {"X": 1, "Y": 2, "Z": 3}, "InteractionType": "WalkTo"},
              {"Position": [4, 5, 6], "TerritoryId": 7, "InteractionType": "WalkTo"}]}
            """)!;

        Assert.Equal((5u, new Vector3(1, 2, 3)), (sequence.Steps[0].TerritoryId, sequence.Steps[0].Position!.Value));
        Assert.Equal((7u, new Vector3(4, 5, 6)), (sequence.Steps[1].TerritoryId, sequence.Steps[1].Position!.Value));
        Assert.Null(sequence.TerritoryId);
    }

    [Fact]
    public void Writer_MovesZoneUpOneLinePositions()
    {
        QuestRoot root = new()
        {
            Author = ["Test"],
            QuestSequence =
            [
                QuestTestData.Seq(1,
                    new QuestStep(EInteractionType.WalkTo, null, new Vector3(1, 2, 3), 7),
                    new QuestStep(EInteractionType.WalkTo, null, new Vector3(1, 2, 3), 5),
                    new QuestStep(EInteractionType.WalkTo, null, new Vector3(1, 2, 3), 5))
            ]
        };

        JsonObject file = JsonNode.Parse(QuestPathJson.Write(
            (JsonObject)JsonSerializer.SerializeToNode(root, JsonOptions.Default)!, JsonOptions.Default))!.AsObject();
        JsonNode sequence = file["QuestSequence"]![0]!;

        Assert.Equal(5u, sequence["TerritoryId"]!.GetValue<uint>());
        Assert.Equal([7u, 0u, 0u], sequence["Steps"]!.AsArray().Select(x => x!["TerritoryId"]?.GetValue<uint>() ?? 0));
        Assert.Contains("\"Position\": [1, 2, 3]", QuestPathJson.Write(
            (JsonObject)JsonSerializer.SerializeToNode(root, JsonOptions.Default)!, JsonOptions.Default), StringComparison.Ordinal);
    }
}
