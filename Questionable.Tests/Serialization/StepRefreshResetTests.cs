using Newtonsoft.Json.Linq;
using Questionable;
using Xunit;

namespace Questionable.Tests.Serialization;

public sealed class StepRefreshResetTests
{
    [Fact]
    public void Reset_TurnsOffBaseAndProfiles()
    {
        var configuration = new Configuration();
        configuration.BaseGeneral.AutoStepRefreshEnabled = true;
        configuration.Profiles["a"] = new()
        {
            ["General"] = new JObject { ["AutoStepRefreshEnabled"] = true },
        };
        configuration.Profiles["b"] = new()
        {
            ["General"] = new JObject { ["AutoStepRefreshEnabled"] = true, ["UseEscToCancelQuesting"] = false },
        };

        configuration.ResetAutoStepRefresh();

        Assert.False(configuration.BaseGeneral.AutoStepRefreshEnabled);
        Assert.False(configuration.Profiles["a"].ContainsKey("General"));
        JObject kept = configuration.Profiles["b"]["General"];
        Assert.False(kept.ContainsKey("AutoStepRefreshEnabled"));
        Assert.True(kept.ContainsKey("UseEscToCancelQuesting"));
    }
}
