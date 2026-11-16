using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Model.Common;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class EquipRecommendedTests
{
    private readonly TestClock _clock = new();
    private readonly ICondition _condition = Substitute.For<ICondition>();
    private readonly Configuration _configuration = new();
    private readonly EquipRecommended.EquipExecutor _executor;
    private int _requests;

    public EquipRecommendedTests()
    {
        _configuration.General.GearsetUpdateSource = EGearsetUpdateSource.Stylist;
        _executor = new(null!, _condition, _configuration, null!,
            NullLogger<EquipRecommended.EquipExecutor>.Instance, _clock);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BusyStylist_TimesOutWithoutNewRequest(bool submitted)
    {
        if (submitted)
            Assert.Equal(ETaskResult.StillRunning, Update(busy: false));

        Assert.Equal(ETaskResult.StillRunning, Update(busy: true));
        _clock.Advance(TimeSpan.FromMilliseconds(29999));
        Assert.Equal(ETaskResult.StillRunning, Update(busy: true));
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        var error = Assert.Throws<NeedsPlayerException>(() => Update(busy: true));

        Assert.Contains("Stylist stayed busy for 30 seconds", error.Message);
        Assert.Contains("restart Questionable", error.Message);
        Assert.Equal(submitted ? 1 : 0, _requests);
        Assert.Equal(EGearsetUpdateSource.Stylist, _configuration.General.GearsetUpdateSource);
    }

    [Fact]
    public void InitialBusyWait_KeepsFullTimeout()
    {
        Update(busy: true);
        _clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(ETaskResult.StillRunning, Update(busy: false));
        Assert.Equal(1, _requests);

        Update(busy: true);
        _clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(ETaskResult.StillRunning, Update(busy: true));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Throws<NeedsPlayerException>(() => Update(busy: true));
        Assert.Equal(1, _requests);
    }

    [Fact]
    public void Restart_ClearsBusyWaitAndRequest()
    {
        Update(busy: false);
        Update(busy: true);
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Throws<NeedsPlayerException>(() => Update(busy: true));

        // Exercise Start's reset without calling native gear modules.
        _condition[ConditionFlag.InCombat].Returns(true);
        Assert.False(_executor.Start(new EquipRecommended.EquipTask()));
        Assert.Equal(ETaskResult.StillRunning, Update(busy: true));
        Assert.Equal(ETaskResult.StillRunning, Update(busy: false));
        Assert.Equal(2, _requests);
    }

    [Fact]
    public void UnchangedGear_MatchesSavedSet()
    {
        var saved = new RaptureGearsetModule.GearsetEntry { ClassJob = 31 };
        var equipped = new InventoryItem[14];
        for (int slot = 0; slot < equipped.Length; ++slot)
        {
            saved.Items[slot].ItemId = (uint)(100 + slot);
            equipped[slot].ItemId = (uint)(100 + slot);
        }

        Assert.True(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
    }

    [Fact]
    public void DurabilityLoss_DoesNotNeedSave()
    {
        var saved = new RaptureGearsetModule.GearsetEntry { ClassJob = 31 };
        var equipped = new InventoryItem[14];
        saved.Items[0].ItemId = equipped[0].ItemId = 123;
        equipped[0].Condition = 30000;
        Assert.True(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
        equipped[0].Condition = 29000;
        Assert.True(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void ChangedSlot_NeedsSave(int slot)
    {
        var saved = new RaptureGearsetModule.GearsetEntry { ClassJob = 31 };
        var equipped = new InventoryItem[14];
        equipped[slot].ItemId = 123;
        Assert.False(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
        saved.Items[slot].ItemId = 123;
        Assert.True(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
        equipped[slot].ItemId = 0;
        Assert.False(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
    }

    [Fact]
    public void ChangedJob_NeedsSave()
    {
        var saved = new RaptureGearsetModule.GearsetEntry { ClassJob = 31 };
        Assert.False(EquipRecommended.GearsetMatches(ref saved, new InventoryItem[14], 8));
    }

    [Fact]
    public void Quality_MatchesEncodedSavedId()
    {
        var saved = new RaptureGearsetModule.GearsetEntry { ClassJob = 31 };
        var equipped = new InventoryItem[14];
        saved.Items[0].ItemId = 123;
        equipped[0].ItemId = 123;
        equipped[0].Flags = InventoryItem.ItemFlags.HighQuality;
        Assert.False(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
        saved.Items[0].ItemId = 1_000_123;
        Assert.True(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AppearanceChange_NeedsSave(int field)
    {
        var saved = new RaptureGearsetModule.GearsetEntry { ClassJob = 31 };
        var equipped = new InventoryItem[14];
        saved.Items[0].ItemId = equipped[0].ItemId = 123;
        if (field == 0)
            equipped[0].GlamourId = 456;
        else
            equipped[0].Stains[field - 1] = 7;
        Assert.False(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
        saved.Items[0].GlamourId = equipped[0].GlamourId;
        saved.Items[0].Stain0Id = equipped[0].Stains[0];
        saved.Items[0].Stain1Id = equipped[0].Stains[1];
        Assert.True(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MateriaChange_NeedsSave(int slot)
    {
        var saved = new RaptureGearsetModule.GearsetEntry { ClassJob = 31 };
        var equipped = new InventoryItem[14];
        saved.Items[0].ItemId = equipped[0].ItemId = 123;
        equipped[0].Materia[slot] = 13;
        Assert.False(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
        saved.Items[0].Materia[slot] = 13;
        Assert.True(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
        equipped[0].MateriaGrades[slot] = 5;
        Assert.False(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
        saved.Items[0].MateriaGrades[slot] = 5;
        Assert.True(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
    }

    [Fact]
    public void EmptySlots_IgnoreUnusedFields()
    {
        var saved = new RaptureGearsetModule.GearsetEntry { ClassJob = 31 };
        var equipped = new InventoryItem[14];
        equipped[0].Flags = InventoryItem.ItemFlags.HighQuality;
        equipped[0].GlamourId = 456;
        saved.Items[0].Stain0Id = 7;
        Assert.True(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
    }

    [Fact]
    public void IncompleteEquipment_NeedsSave()
    {
        var saved = new RaptureGearsetModule.GearsetEntry { ClassJob = 31 };
        Assert.False(EquipRecommended.GearsetMatches(ref saved, new InventoryItem[13], 31));
    }

    [Fact]
    public void SymbolicEquipment_NeedsSave()
    {
        var saved = new RaptureGearsetModule.GearsetEntry { ClassJob = 31 };
        var equipped = new InventoryItem[14];
        equipped[0].IsSymbolic = true;
        Assert.False(EquipRecommended.GearsetMatches(ref saved, equipped, 31));
    }

    private ETaskResult Update(bool busy) => _executor.UpdateStylist(busy, () =>
    {
        ++_requests;
        return true;
    });
}

public sealed class EquipmentSlotsTests
{
    [Theory]
    [InlineData(1u, InventoryType.ArmoryMainHand)]
    [InlineData(2u, InventoryType.ArmoryOffHand)]
    [InlineData(3u, InventoryType.ArmoryHead)]
    [InlineData(4u, InventoryType.ArmoryBody)]
    [InlineData(5u, InventoryType.ArmoryHands)]
    [InlineData(6u, null)]
    [InlineData(7u, InventoryType.ArmoryLegs)]
    [InlineData(8u, InventoryType.ArmoryFeets)]
    [InlineData(9u, InventoryType.ArmoryEar)]
    [InlineData(10u, InventoryType.ArmoryNeck)]
    [InlineData(11u, InventoryType.ArmoryWrist)]
    [InlineData(12u, InventoryType.ArmoryRings)]
    [InlineData(13u, InventoryType.ArmoryMainHand)]
    [InlineData(14u, null)]
    [InlineData(17u, InventoryType.ArmorySoulCrystal)]
    public void ArmoryFor_MatchesSlotCategory(uint categoryId, InventoryType? expected)
    {
        Assert.Equal(expected, EquipmentSlots.ArmoryFor(categoryId));
    }
}
