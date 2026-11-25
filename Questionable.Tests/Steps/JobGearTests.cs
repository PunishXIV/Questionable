using Questionable.Controller.Steps.Shared;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class JobGearTests
{
    private static JobGear.Option Set(int level, int itemLevel, bool unlocked = true, bool collected = false) =>
        new(level, itemLevel, unlocked, collected);

    private static int? Choose(int playerLevel, int equipped, params JobGear.Option[] options) =>
        JobGear.Choose(options, playerLevel, equipped)?.Level;

    [Fact]
    public void Choose_HighestWearableSet() =>
        Assert.Equal(89, Choose(95, 500, Set(99, 690), Set(89, 560), Set(80, 430)));

    [Fact]
    public void Choose_SkipsLockedSet() =>
        Assert.Equal(89, Choose(100, 500, Set(99, 690, unlocked: false), Set(89, 560)));

    [Fact]
    public void Choose_NoFallbackAfterCollected() =>
        Assert.Null(Choose(100, 300, Set(99, 690, collected: true), Set(89, 560)));

    [Fact]
    public void Choose_NothingWithoutUpgrade() =>
        Assert.Null(Choose(100, 700, Set(99, 690), Set(89, 560)));

    [Fact]
    public void Choose_NothingBelowSetLevel() =>
        Assert.Null(Choose(79, 300, Set(99, 690), Set(89, 560), Set(80, 430)));
}
