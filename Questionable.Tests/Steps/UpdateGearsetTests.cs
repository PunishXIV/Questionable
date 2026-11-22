using Questionable.Controller.Steps.Shared;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class UpdateGearsetTests
{
    private const byte DarkKnight = 32;
    private const byte Beastmaster = 42;

    [Fact]
    public void OtherJobCurrent_PicksJobSet()
    {
        byte?[] slots = [19, null, DarkKnight, Beastmaster];
        Assert.Equal(3, UpdateGearset.Executor.PickGearset(slots, Beastmaster, current: 2));
    }

    [Fact]
    public void MatchingCurrent_KeepsCurrent()
    {
        byte?[] slots = [Beastmaster, DarkKnight, Beastmaster];
        Assert.Equal(2, UpdateGearset.Executor.PickGearset(slots, Beastmaster, current: 2));
    }

    [Fact]
    public void NoSetForJob_ReturnsNone()
    {
        byte?[] slots = [DarkKnight, null];
        Assert.Equal(-1, UpdateGearset.Executor.PickGearset(slots, Beastmaster, current: 0));
    }
}
