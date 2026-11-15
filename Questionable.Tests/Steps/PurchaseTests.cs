using Questionable.Controller.Steps.Shared;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class PurchaseTests
{
    [Fact]
    public void ShortGil_NamesPriceAndGil()
    {
        Assert.Equal("Not enough gil to buy 1x Leather Eyepatch: it costs 141 gil and you have 138.",
            Purchase.GilShortage(141, 1, 138, "Leather Eyepatch"));
    }

    [Theory]
    [InlineData(141u, 1, 141u)]
    [InlineData(10u, 3, 500u)]
    public void EnoughGil_NoShortage(uint price, int count, uint gil) =>
        Assert.Null(Purchase.GilShortage(price, count, gil, "item"));

    [Fact]
    public void ShortGil_CountsEveryItem() =>
        Assert.NotNull(Purchase.GilShortage(100, 3, 299, "item"));

    [Fact]
    public void HugeCount_DoesNotOverflow() =>
        Assert.NotNull(Purchase.GilShortage(uint.MaxValue, 2, uint.MaxValue, "item"));
}
