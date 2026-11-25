using Newtonsoft.Json.Linq;
using Questionable.Controller.Steps.Common;
using Questionable.Model.Common;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class MailTests
{
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
