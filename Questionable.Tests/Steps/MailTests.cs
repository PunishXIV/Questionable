using Newtonsoft.Json.Linq;
using Questionable.Controller.Steps.Common;
using Questionable.Model.Common;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class MailTests
{
    private static Mail.Moogle? Route(byte startTown, params EAetheryteLocation[] unlocked) =>
        Mail.ChooseRoute(startTown, unlocked.Contains);

    [Fact]
    public void ChooseRoute_StartingCityFirst()
    {
        EAetheryteLocation[] all = [EAetheryteLocation.Limsa, EAetheryteLocation.Gridania, EAetheryteLocation.Uldah];

        Assert.Equal(EAetheryteLocation.Uldah, Route(3, all)?.City);
        Assert.Equal(EAetheryteLocation.Gridania, Route(2, all)?.City);
    }

    [Fact]
    public void ChooseRoute_SkipsLockedCity() =>
        Assert.Equal(EAetheryteLocation.Gridania, Route(1, EAetheryteLocation.Gridania)?.City);

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
