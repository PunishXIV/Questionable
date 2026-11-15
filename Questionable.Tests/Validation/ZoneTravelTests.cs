using Questionable.Model.Common;
using Questionable.Validation;
using Xunit;

namespace Questionable.Tests.Validation;

public sealed class ZoneTravelTests
{
    private static readonly Dictionary<EAetheryteLocation, uint> Territories = new()
    {
        { EAetheryteLocation.Gridania, 132 },
        { EAetheryteLocation.GridaniaAmphitheatre, 133 },
        { EAetheryteLocation.Limsa, 129 },
    };

    private static readonly Dictionary<EAetheryteLocation, ushort> Groups = new()
    {
        { EAetheryteLocation.Gridania, 2 },
        { EAetheryteLocation.GridaniaAmphitheatre, 2 },
        { EAetheryteLocation.Limsa, 1 },
    };

    private static ZoneTravel.Way Pick(uint territory, bool lifestream = true, params EAetheryteLocation[] locked) =>
        ZoneTravel.Pick(territory, Territories, Groups, x => !locked.Contains(x), lifestream);

    [Fact]
    public void MainAetheryte_Teleports() =>
        Assert.Equal(new ZoneTravel.Way(EAetheryteLocation.Gridania, null, null), Pick(132));

    [Fact]
    public void ShardOnlyZone_HopsFromCity() =>
        Assert.Equal(new ZoneTravel.Way(EAetheryteLocation.Gridania, EAetheryteLocation.GridaniaAmphitheatre, null),
            Pick(133));

    [Fact]
    public void ShardWithoutLifestream_Skips() =>
        Assert.Equal("needs Lifestream", Pick(133, lifestream: false).Skip);

    [Fact]
    public void LockedCity_IsNotAttuned() =>
        Assert.Equal("not attuned", Pick(133, true, EAetheryteLocation.Gridania).Skip);

    [Fact]
    public void LockedAetheryte_IsNotAttuned() =>
        Assert.Equal("not attuned", Pick(129, true, EAetheryteLocation.Limsa).Skip);

    [Fact]
    public void NoAetheryte_Skips() =>
        Assert.Equal("no aetheryte", Pick(204).Skip);

    [Fact]
    public void Order_CurrentFirstSkippedLast()
    {
        List<(uint Id, ZoneTravel.Way Way)> zones =
        [
            (204, new ZoneTravel.Way(null, null, "no aetheryte")),
            (133, new ZoneTravel.Way(EAetheryteLocation.Gridania, EAetheryteLocation.GridaniaAmphitheatre, null)),
            (132, new ZoneTravel.Way(EAetheryteLocation.Gridania, null, null)),
            (129, new ZoneTravel.Way(EAetheryteLocation.Limsa, null, null)),
        ];

        List<uint> order = ZoneTravel.Order(zones, x => x.Id, x => x.Way, current: 129).Select(x => x.Id).ToList();

        Assert.Equal([129, 132, 133, 204], order);
    }
}
