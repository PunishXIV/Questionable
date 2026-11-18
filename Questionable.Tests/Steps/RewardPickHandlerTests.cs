using Questionable.Controller.GameUi;
using Xunit;
using ERewardRule = Questionable.Configuration.ERewardRule;
using Option = Questionable.Controller.GameUi.RewardPickHandler.Option;

namespace Questionable.Tests.Steps;

public sealed class RewardPickHandlerTests
{
    private static readonly Option Gil = new(1, false, 0, false, false, 900);
    private static readonly Option Minion = new(2, true, 0, false, false, 10);
    private static readonly Option Upgrade = new(3, false, 12, false, false, 200);
    private static readonly Option Coffer = new(4, false, 0, true, false, 50);

    private static int Pick(Option[] options, ERewardRule[]? order = null, params ERewardRule[] off) =>
        RewardPickHandler.Choose(options, order ?? [], off.ToHashSet());

    [Fact]
    public void DefaultOrder_CollectibleFirst() =>
        Assert.Equal(1, Pick([Gil, Minion, Upgrade, Coffer]));

    [Fact]
    public void ChangedOrder_UpgradeBeforeCollectible() =>
        Assert.Equal(2, Pick([Gil, Minion, Upgrade, Coffer], [ERewardRule.GearUpgrade]));

    [Fact]
    public void RuleOff_IsSkipped() =>
        Assert.Equal(3, Pick([Gil, Minion, Upgrade, Coffer], null, ERewardRule.UnownedCollectible, ERewardRule.GearUpgrade));

    [Fact]
    public void NoMatch_HighestVendorValue() =>
        Assert.Equal(0, Pick([Gil, Coffer with { Coffer = false }], null, ERewardRule.VendorValue));

    [Fact]
    public void NothingValuable_FirstOption() =>
        Assert.Equal(0, Pick([Gil with { VendorValue = 0 }, Coffer with { Coffer = false, VendorValue = 0 }]));
}
