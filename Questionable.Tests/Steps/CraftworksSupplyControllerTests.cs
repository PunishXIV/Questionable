using FFXIVClientStructs.FFXIV.Client.Game;
using Questionable.Controller.GameUi;
using Xunit;
using Candidate = Questionable.Controller.GameUi.CraftworksSupplyController.Candidate;

namespace Questionable.Tests.Steps;

public sealed class CraftworksSupplyControllerTests
{
    [Fact]
    public void Pick_SkipsHandedTakesBest()
    {
        Candidate low = new(InventoryType.Inventory1, 3, false, 610);
        Candidate high = new(InventoryType.Inventory2, 0, false, 650);
        Candidate hq = new(InventoryType.Inventory4, 7, true, 0);

        Assert.Equal(hq, CraftworksSupplyController.Pick([low, high, hq], new HashSet<(InventoryType, short)>()));
        Assert.Equal(high, CraftworksSupplyController.Pick([low, high], new HashSet<(InventoryType, short)>()));
        Assert.Equal(low, CraftworksSupplyController.Pick([low, high],
            new HashSet<(InventoryType, short)> { (InventoryType.Inventory2, 0) }));
        Assert.Null(CraftworksSupplyController.Pick([low],
            new HashSet<(InventoryType, short)> { (InventoryType.Inventory1, 3) }));
    }
}
