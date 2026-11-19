// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Questionable.AutoGen.Generation;
using Questionable.Model.Questing;
using Xunit;
using Xunit.Abstractions;

namespace Questionable.Tests.AutoGen;

/// <summary>
///     Pins the derivation rules in <see cref="QuestPathAutoGenerator"/> against quests whose shape exercises
///     them. Each case here corresponds to a bug that was found by running the generated path in-game, so a
///     regression in any of these rules means someone's quest silently stops working again.
///     <para>
///         Requires an installed game — see <see cref="GameDataFixture"/>; without one the tests return early.
///     </para>
/// </summary>
public sealed class QuestPathGenerationTest(GameDataFixture fixture, ITestOutputHelper output)
    : IClassFixture<GameDataFixture>
{
    private static readonly JsonSerializerOptions ReloadOptions = new() { PropertyNameCaseInsensitive = false };

    /// <summary>Generates a quest, or returns null when there is no game data to generate from.</summary>
    private QuestRoot? Generate(ushort questId)
    {
        if (!fixture.Available)
        {
            output.WriteLine(
                $"Skipped: no FFXIV installation found (set {GameDataFixture.GamePathEnvVar} to run these).");
            return null;
        }

        QuestPathResult? result = fixture.Factory!.GenerateById(questId, GameDataFixture.Author);
        Assert.NotNull(result);
        return result.Root;
    }

    private static QuestSequence SequenceOf(QuestRoot root, byte sequence) =>
        Assert.Single(root.QuestSequence, x => x.Sequence == sequence);

    [Fact]
    public void Quest481_UsesTheLiceCombRatherThanTalkingToSheep()
    {
        // The script never declares IsEventItemUsable; only the journal's "Use the lice comb on the shaggy
        // sheep" says so, which is what the todo-text fallback exists for.
        if (Generate(481) is not { } root)
            return;

        QuestSequence sequence = SequenceOf(root, 2);
        Assert.NotEmpty(sequence.Steps);
        Assert.All(sequence.Steps, step =>
        {
            Assert.Equal(EInteractionType.UseItem, step.InteractionType);
            Assert.Equal(2000437u, step.ItemId);
        });
    }

    [Fact]
    public void Quest1914_PerformsTheEmoteTheJournalAsksFor()
    {
        // "/dance for Aanu Vanu" - the emote appears in neither QuestParams nor any Lua function.
        if (Generate(1914) is not { } root)
            return;

        foreach (byte sequenceNumber in new byte[] { 1, 2 })
        {
            QuestStep step = Assert.Single(SequenceOf(root, sequenceNumber).Steps);
            Assert.Equal(EInteractionType.Emote, step.InteractionType);
            Assert.Equal(EEmote.Dance, step.Emote);
        }
    }

    [Fact]
    public void Quest1915_UsesTheSaddleAtTheSequenceThatConsumesIt()
    {
        // The saddle is held across sequences 1 and 2: picked up at 1, used at 2. Using it starts the fight,
        // so the combat step is AfterItemUse rather than AfterInteraction.
        if (Generate(1915) is not { } root)
            return;

        QuestStep pickUp = Assert.Single(SequenceOf(root, 1).Steps);
        Assert.Equal(EInteractionType.Interact, pickUp.InteractionType);

        QuestStep use = Assert.Single(SequenceOf(root, 2).Steps);
        Assert.Equal(EInteractionType.Combat, use.InteractionType);
        Assert.Equal(EEnemySpawnType.AfterItemUse, use.EnemySpawnType);
        Assert.Equal(2001761u, use.ItemId);
    }

    [Fact]
    public void Quest1916_DoesNotInteractWithTheQuestGiverBeforeAHunt()
    {
        // The script lists an actor alongside the enemies only because they stay targetable; the fight is the
        // objective and the combat step anchors to them, so there is no interaction step in front of it.
        if (Generate(1916) is not { } root)
            return;

        QuestStep step = Assert.Single(SequenceOf(root, 3).Steps);
        Assert.Equal(EInteractionType.Combat, step.InteractionType);
    }

    [Fact]
    public void Quest1919_KeepsFightingUntilTheKeyDrops()
    {
        // Without a drop condition the fight ends after a single kill. How many are needed comes from the
        // event item's stack size, not the todo quantity.
        if (Generate(1919) is not { } root)
            return;

        QuestStep fight = Assert.Single(SequenceOf(root, 1).Steps);
        Assert.Equal(EInteractionType.Combat, fight.InteractionType);
        Assert.NotEmpty(fight.ComplexCombatData);
        Assert.All(fight.ComplexCombatData, data =>
        {
            Assert.Equal(2001672u, data.RewardItemId);
            Assert.Equal(1, data.RewardItemCount);
        });

        QuestStep use = Assert.Single(SequenceOf(root, 3).Steps);
        Assert.Equal(EInteractionType.UseItem, use.InteractionType);
        Assert.Equal(2001672u, use.ItemId);
    }

    [Fact]
    public void Quest1741_VisitsEveryNpcOfAMultiTargetSequence()
    {
        // "Speak with all three": the todo entries are bare map ranges, so each actor has to be matched to an
        // area by position. Taking only the first actor leaves the path stuck on an NPC already spoken to.
        if (Generate(1741) is not { } root)
            return;

        List<uint> targets = [.. SequenceOf(root, 1).Steps.Select(x => x.DataId ?? 0)];
        Assert.Equal([1012057u, 1012056u, 1012053u], targets);
    }

    [Fact]
    public void Quest480_PlacesActorsTheLevelSheetHasNoRowFor()
    {
        // The three bolting dodos are quest-only actors: the Level sheet never mentions them and the journal
        // only gives a search area with a 103y radius. Standing at the middle of that circle leaves the item
        // out of range, so the positions have to come from the zone's layer data instead.
        if (Generate(480) is not { } root)
            return;

        Vector3[] dodos =
        [
            new(574.77f, 87.50f, -34.42f),
            new(565.46f, 84.45f, -115.97f),
            new(478.68f, 81.16f, -38.10f)
        ];

        List<QuestStep> steps = SequenceOf(root, 2).Steps;
        Assert.Equal(3, steps.Count);
        Assert.Equal([1002661u, 1002687u, 1002688u], steps.Select(x => x.DataId ?? 0));
        Assert.All(steps.Zip(dodos), pair =>
        {
            Assert.Equal(EInteractionType.UseItem, pair.First.InteractionType);
            Assert.NotNull(pair.First.Position);
            Assert.True(Vector3.Distance(pair.First.Position!.Value, pair.Second) < 1f,
                $"{pair.First.DataId} is at {pair.First.Position}, expected {pair.Second}");
        });
    }

    [Fact]
    public void Quest1709_FindsEveryEnemyOfAMultiFightSequence()
    {
        // Four interact-then-fight pairs across four zones; applying combat only to the primary actor found
        // half of them.
        if (Generate(1709) is not { } root)
            return;

        HashSet<uint> enemies =
        [
            .. root.QuestSequence
                .SelectMany(x => x.Steps)
                .SelectMany(x => x.KillEnemyDataIds.Concat(x.ComplexCombatData.Select(y => y.DataId)))
        ];

        Assert.Superset(new HashSet<uint> { 9487u, 9488u, 9489u, 9490u }, enemies);
    }

    [Fact]
    public void GeneratedPathsAreLoadableByThePlugin()
    {
        // Round-trips the writer's output back through the model the plugin loads paths with, which catches
        // serialization changes that would produce files the registry cannot read.
        if (!fixture.Available)
        {
            output.WriteLine(
                $"Skipped: no FFXIV installation found (set {GameDataFixture.GamePathEnvVar} to run these).");
            return;
        }

        foreach (ushort questId in new ushort[] { 481, 1200, 1709, 1741, 1914, 1915, 1916, 1919 })
        {
            QuestPathResult? result = fixture.Factory!.GenerateById(questId, GameDataFixture.Author);
            Assert.NotNull(result);

            string json = QuestPathWriter.Serialize(result);
            QuestRoot? reloaded = JsonSerializer.Deserialize<QuestRoot>(json, ReloadOptions);

            Assert.NotNull(reloaded);
            Assert.NotEmpty(reloaded.QuestSequence);
            Assert.Contains(reloaded.QuestSequence, x => x.Sequence == 0);
            Assert.Contains(reloaded.QuestSequence, x => x.Sequence == 255);
        }
    }
}
