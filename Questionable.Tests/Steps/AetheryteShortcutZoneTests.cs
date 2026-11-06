using Questionable.Controller.Steps.Shared;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class AetheryteShortcutZoneTests
{
    [Theory]
    [InlineData(351u, 351u, 156u, true)]
    [InlineData(156u, 351u, 156u, false)]
    [InlineData(130u, 351u, 156u, false)]
    [InlineData(956u, 956u, 956u, false)]
    public void TeleportIsSkippedOnlyWhenItWouldLeaveTheStepsZone(uint player, uint step, uint teleport, bool skip)
    {
        Assert.Equal(skip, AetheryteShortcut.IsAlreadyInStepTerritory(player, step, teleport));
    }
}
