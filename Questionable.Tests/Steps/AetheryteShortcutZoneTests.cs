using System.Numerics;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class AetheryteShortcutZoneTests
{
    [Theory]
    [InlineData(351u, 351u, 156u, true)]
    [InlineData(156u, 351u, 156u, false)]
    [InlineData(130u, 351u, 156u, false)]
    [InlineData(956u, 956u, 956u, false)]
    public void Teleport_SkippedOnlyIfLeavingZone(uint player, uint step, uint teleport, bool skip)
    {
        Assert.Equal(skip, AetheryteShortcut.IsAlreadyInStepTerritory(player, step, teleport));
    }

    private static readonly Vector3 QuestGiver = new(-441.6f, 23.7f, -359f);

    [Theory]
    [InlineData(177u, 140u, 1, true)]
    [InlineData(177u, 212u, 1, true)]
    [InlineData(140u, 140u, 1, false)]
    [InlineData(212u, 140u, 1, false)]
    [InlineData(177u, 140u, 0, false)]
    public void VesperBayStep_TakesTicket(uint player, uint step, int tickets, bool ticket) =>
        Assert.Equal(ticket, AetheryteShortcut.TakesVesperBayTicket(
            EAetheryteLocation.WesternThanalanHorizon, player, step, QuestGiver, horizonDistance: 480, tickets));

    [Fact]
    public void HorizonStep_TeleportsToHorizon() =>
        Assert.False(AetheryteShortcut.TakesVesperBayTicket(
            EAetheryteLocation.WesternThanalanHorizon, 177, 140, new Vector3(70, 0, 0), horizonDistance: 20, 1));

    [Fact]
    public void OtherAetheryte_NoTicket() =>
        Assert.False(AetheryteShortcut.TakesVesperBayTicket(
            EAetheryteLocation.Limsa, 177, 140, QuestGiver, horizonDistance: 480, 1));
}
