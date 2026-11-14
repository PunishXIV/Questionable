using Newtonsoft.Json.Linq;
using Questionable.Controller.Steps.Common;
using Questionable.Model.Common;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class MailTests
{
    private static readonly HashSet<EAetheryteLocation> Shards =
        [EAetheryteLocation.LimsaAftcastle, EAetheryteLocation.GridaniaAmphitheatre];

    private static (Mail.Moogle Moogle, EAetheryteLocation? Shard)? Route(byte startTown,
        params EAetheryteLocation[] unlocked) =>
        Mail.ChooseRoute(startTown,
            x => x.City switch
            {
                EAetheryteLocation.Limsa => EAetheryteLocation.LimsaAftcastle,
                EAetheryteLocation.Gridania => EAetheryteLocation.GridaniaAmphitheatre,
                _ => null,
            },
            x => x.City != EAetheryteLocation.Uldah,
            unlocked.Contains);

    [Fact]
    public void ChooseRoute_StartingCityFirst()
    {
        EAetheryteLocation[] all =
        [
            EAetheryteLocation.Limsa, EAetheryteLocation.Gridania, EAetheryteLocation.Uldah, .. Shards,
        ];

        Assert.Equal(EAetheryteLocation.Uldah, Route(3, all)?.Moogle.City);
        Assert.Equal((EAetheryteLocation.Gridania, EAetheryteLocation.GridaniaAmphitheatre),
            Route(2, all) is { } route ? (route.Moogle.City, route.Shard) : default);
    }

    [Fact]
    public void ChooseRoute_SkipsLockedShard()
    {
        var route = Route(1, EAetheryteLocation.Limsa, EAetheryteLocation.Gridania,
            EAetheryteLocation.GridaniaAmphitheatre);

        Assert.Equal(EAetheryteLocation.Gridania, route?.Moogle.City);
    }

    [Fact]
    public void ChooseRoute_NothingUnlocked_Null() =>
        Assert.Null(Route(1, EAetheryteLocation.LimsaAftcastle));

    [Fact]
    public void TurnOnClaimMail_BaseAndProfiles()
    {
        var configuration = new Configuration();
        configuration.BaseGeneral.ClaimMail = false;
        configuration.Profiles["a"] = new() { ["General"] = new JObject { ["ClaimMail"] = false } };
        configuration.Profiles["b"] = new()
        {
            ["General"] = new JObject { ["ClaimMail"] = false, ["UseEscToCancelQuesting"] = false },
        };

        configuration.TurnOnClaimMail();

        Assert.True(configuration.BaseGeneral.ClaimMail);
        Assert.False(configuration.Profiles["a"].ContainsKey("General"));
        Assert.False(configuration.Profiles["b"]["General"].ContainsKey("ClaimMail"));
        Assert.True(configuration.Profiles["b"]["General"].ContainsKey("UseEscToCancelQuesting"));
    }
}
