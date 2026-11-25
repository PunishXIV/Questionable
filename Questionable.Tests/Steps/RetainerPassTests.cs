using Questionable.Controller;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class RetainerPassTests
{
    private static readonly AutoRetainerIpc.Character[] Characters =
    [
        new() { ContentId = 1, Enabled = true },
        new() { ContentId = 2, WorkshopEnabled = true },
        new() { ContentId = 3 },
    ];

    private static bool AnyReady(ulong[] retainersBack, ulong[] vesselsBack, AutoRetainerIpc.EPassType? type = null) =>
        RetainerPass.AnyReady(Characters, retainersBack.Contains, vesselsBack.Contains, type);

    [Fact]
    public void AnyReady_RetainersBack() => Assert.True(AnyReady([1], []));

    [Fact]
    public void AnyReady_VesselsBack() => Assert.True(AnyReady([], [2]));

    [Fact]
    public void AnyReady_NeedsWorkshopForVessels() => Assert.False(AnyReady([], [1]));

    [Fact]
    public void AnyReady_SkipsNonMultiMode() => Assert.False(AnyReady([3], [3]));

    [Fact]
    public void AnyReady_OnlyCountsChosenKind()
    {
        Assert.False(AnyReady([1], [], AutoRetainerIpc.EPassType.Submersibles));
        Assert.True(AnyReady([], [2], AutoRetainerIpc.EPassType.Submersibles));
        Assert.False(AnyReady([], [2], AutoRetainerIpc.EPassType.Retainers));
        Assert.True(AnyReady([1], [], AutoRetainerIpc.EPassType.Retainers));
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 30)]
    public void NextPass_WaitsAfterEveryPass(bool leftOver, int minutes)
    {
        DateTime now = new(2026, 11, 22, 17, 21, 29);
        Assert.Equal(now.AddMinutes(minutes), RetainerPass.NextPass(now, leftOver));
    }

    [Theory]
    [InlineData(0u, 628u, 0, 628u)]
    [InlineData(0u, 155u, 2, 133u)]
    [InlineData(0u, 155u, 3, 131u)]
    [InlineData(0u, 155u, 0, 129u)]
    [InlineData(962u, 628u, 1, 962u)]
    public void Bell_PrefersChoiceThenZone(uint preference, uint territory, byte grandCompany, uint expected) =>
        Assert.Equal(expected, Questionable.Controller.Steps.Common.SummoningBell.Pick(preference, territory, grandCompany).TerritoryId);
}
