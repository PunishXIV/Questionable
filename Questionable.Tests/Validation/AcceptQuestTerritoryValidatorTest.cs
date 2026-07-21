// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @alydevs

using NSubstitute;
using Questionable.Data;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Validation;
using Questionable.Validation.Validators;
using Xunit;

using static Questionable.Tests.TestData.QuestTestData;

namespace Questionable.Tests.Validation;

public sealed class AcceptQuestTerritoryValidatorTest
{
    // Two known aetheryte territories the validator should consider "valid"
    private const uint LimsaTerritory = 129;
    private const uint GridaniaTerritory = 132;

    // The Dravanian Hinterlands, a territory that has no aetheryte (so an AcceptQuest there must rely on a shortcut)
    private const uint NoAetheryteTerritory = 399;

    // Some territories have no aetheryte, but must have custom recovery steps leading up to an AcceptQuest step.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2211:Non-constant fields should not be visible", Justification = "MemberData must reference a public member")]
    public static TheoryData<uint, uint> RecoveryTerritoryData = new()
    {
        { 212, 140 }, // Waking Sands acceptquest must have Western Thanalan recovery
        { 351, 156 }  // Rising stones must have mor dhona recovery
    };

    // These territories are used for Coming to x/Close to Home; the player is auto-placed
    // there at quest start, so an AcceptQuest step here never needs an Aether(yte|net)Shortcut.
    public static TheoryData<uint> CharacterStartingTerritoryData => new() { 181, 182, 183 };

    private readonly AcceptQuestTerritoryValidator _validator;

    public AcceptQuestTerritoryValidatorTest()
    {
        var provider = Substitute.For<IAetheryteTerritoryProvider>();
        provider.AetheryteTerritoryIds.Returns(new[] { LimsaTerritory, GridaniaTerritory });
        _validator = new AcceptQuestTerritoryValidator(provider);
    }

    // --- helpers ---

    private static QuestStep InteractStep(uint territoryId,
        EAetheryteLocation? aetheryteShortcut = null,
        AethernetShortcut? aethernetShortcut = null) =>
        new()
        {
            InteractionType = EInteractionType.Interact,
            TerritoryId = territoryId,
            AetheryteShortcut = aetheryteShortcut,
            AethernetShortcut = aethernetShortcut,
        };

    private static QuestStep AcceptStep(uint territoryId,
        EAetheryteLocation? aetheryteShortcut = null,
        AethernetShortcut? aethernetShortcut = null) =>
        new()
        {
            InteractionType = EInteractionType.AcceptQuest,
            TerritoryId = territoryId,
            AetheryteShortcut = aetheryteShortcut,
            AethernetShortcut = aethernetShortcut,
        };

    // --- happy paths ---

    [Fact]
    public void AcceptQuest_TerritoryIsAetheryteTerritory_ReturnsNoIssues()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0, AcceptStep(LimsaTerritory)));

        Assert.Empty(_validator.Validate(quest));
    }

    [Fact]
    public void AcceptQuest_UnknownTerritory_ButAetheryteShortcutSet_ReturnsNoIssues()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0, AcceptStep(NoAetheryteTerritory, aetheryteShortcut: EAetheryteLocation.Limsa)));

        Assert.Empty(_validator.Validate(quest));
    }

    [Fact]
    public void AcceptQuest_UnknownTerritory_ButAethernetShortcutSet_ReturnsNoIssues()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0, AcceptStep(NoAetheryteTerritory,
                aethernetShortcut: new AethernetShortcut
                {
                    From = EAetheryteLocation.Limsa,
                    To = EAetheryteLocation.LimsaArcanist,
                })));

        Assert.Empty(_validator.Validate(quest));
    }

    // --- the failing case ---

    [Fact]
    public void AcceptQuest_UnknownTerritory_NoShortcuts_ReturnsNone()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0, AcceptStep(NoAetheryteTerritory)));

        var issues = _validator.Validate(quest).ToList();

        Assert.Single(issues);
        Assert.Equal(EIssueType.InvalidAcceptQuestTerritory, issues[0].Type);
        Assert.Equal(EIssueSeverity.None, issues[0].Severity);
        Assert.Equal((byte)0, issues[0].Sequence);
        Assert.Equal(0, issues[0].Step);
    }

    // --- scope: only sequence 0 AcceptQuest steps are checked ---

    [Fact]
    public void NonAcceptQuestStep_InSequence0_IsIgnored()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0, new QuestStep
            {
                InteractionType = EInteractionType.WalkTo,
                TerritoryId = NoAetheryteTerritory,
            }));

        Assert.Empty(_validator.Validate(quest));
    }

    [Fact]
    public void AcceptQuestStep_InNonZeroSequence_IsIgnored()
    {
        // The rule is scoped to sequence 0; other sequences are someone else's concern.
        var quest = CreateQuest(new QuestId(1),
            Seq(0, AcceptStep(LimsaTerritory)),
            Seq(1, AcceptStep(NoAetheryteTerritory)));

        Assert.Empty(_validator.Validate(quest));
    }

    [Fact]
    public void NoSequence0_ReturnsNoIssues()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(255, new QuestStep { InteractionType = EInteractionType.CompleteQuest }));

        Assert.Empty(_validator.Validate(quest));
    }

    // --- known exceptions ---

    [Theory]
    [MemberData(nameof(CharacterStartingTerritoryData))]
    public void AcceptQuest_InCharacterStartingTerritory_NoShortcuts_ReturnsNoIssues(uint territoryId)
    {
        // The player is auto-placed in these territories for the opening class/race quest,
        // so requiring an AetheryteShortcut/AethernetShortcut would be nonsensical.
        var quest = CreateQuest(new QuestId(1),
            Seq(0, AcceptStep(territoryId)));

        Assert.Empty(_validator.Validate(quest));
    }

    [Theory]
    [MemberData(nameof(RecoveryTerritoryData))]
    public void AcceptQuest_RecoveryTerritory_NoShortcuts_ReturnsNoIssues(uint acceptTerritory, uint prevTerritory)
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0, InteractStep(prevTerritory), AcceptStep(acceptTerritory)));

        Assert.Empty(_validator.Validate(quest));
    }

    // --- multiple steps ---

    [Fact]
    public void OnlyOffendingAcceptQuestStepIsReported()
    {
        var quest = CreateQuest(new QuestId(1),
            Seq(0,
                AcceptStep(LimsaTerritory),       // ok
                AcceptStep(NoAetheryteTerritory)  // bad
            ));

        var issues = _validator.Validate(quest).ToList();

        Assert.Single(issues);
        Assert.Equal(1, issues[0].Step);
    }
}
