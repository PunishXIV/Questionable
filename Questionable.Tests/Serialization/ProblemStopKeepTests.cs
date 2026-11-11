using Newtonsoft.Json.Linq;
using Xunit;

namespace Questionable.Tests.Serialization;

public sealed class ProblemStopKeepTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Keep_FollowsStopCommand(bool runCommand)
    {
        var configuration = new Configuration();
        configuration.Stop.RunCommandAfterStop = runCommand;

        configuration.KeepProblemStop();

        Assert.Equal(runCommand, configuration.Stop.RunAfterProblemStop);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Keep_FollowsProfileCommand(bool baseCommand, bool profileCommand)
    {
        var configuration = new Configuration();
        configuration.Stop.RunCommandAfterStop = baseCommand;
        configuration.Profiles["alt"] = new()
        {
            ["Stop"] = new JObject { ["RunCommandAfterStop"] = profileCommand },
        };
        configuration.Profiles["other"] = new()
        {
            ["Stop"] = new JObject { ["TargetLevel"] = 60 },
        };

        configuration.KeepProblemStop();
        configuration.ActivateProfile("alt");
        bool alt = configuration.Stop.RunAfterProblemStop;
        configuration.ActivateProfile("other");
        bool other = configuration.Stop.RunAfterProblemStop;

        Assert.Equal(profileCommand, alt);
        Assert.Equal(baseCommand, other);
        Assert.False(configuration.Profiles["other"]["Stop"].ContainsKey("RunAfterProblemStop"));
    }
}
