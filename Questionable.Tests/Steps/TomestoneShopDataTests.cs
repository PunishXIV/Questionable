using System.Collections.Generic;
using Questionable.Data;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class TomestoneShopDataTests
{
    // Poetics is row 1 in the game data; rows 2 to 4 are the current tomestones.
    private static readonly HashSet<uint> Rows = [1, 2, 3, 4];

    [Fact]
    public void PoeticsShop_IsTomestones() => Assert.True(TomestoneShopData.IsTomestoneCost(1, 4, Rows));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(16)]
    public void OtherShopTypes_AreNot(int shopType) => Assert.False(TomestoneShopData.IsTomestoneCost(1, shopType, Rows));

    [Theory]
    [InlineData(0u)]
    [InlineData(25u)]
    [InlineData(5594u)]
    public void OtherCosts_AreNot(uint costId) => Assert.False(TomestoneShopData.IsTomestoneCost(costId, 4, Rows));
}
