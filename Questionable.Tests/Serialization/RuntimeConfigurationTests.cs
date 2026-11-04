using Newtonsoft.Json;
using Questionable.Controller.Steps.Shared;
using Xunit;

namespace Questionable.Tests.Serialization;

public sealed class RuntimeConfigurationTests : IDisposable
{
    private readonly Configuration _configuration = new();

    public RuntimeConfigurationTests() => Configuration.InitializeRuntime(_configuration);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeConsumers_FollowCharacterOverrides_AfterSave(bool baseEnabled)
    {
        _configuration.Advanced.AutoRedeemRewardItems = baseEnabled;
        _configuration.Advanced.NeverFly = baseEnabled;
        _configuration.Advanced.AutoRedeemItemBlacklist = [100];
        _configuration.CreateProfile("Alt");
        _configuration.AssignProfile("42", "Alt");
        _configuration.ActivateForCharacter("42");
        _configuration.Advanced.AutoRedeemRewardItems = !baseEnabled;
        _configuration.Advanced.NeverFly = !baseEnabled;
        _configuration.Advanced.AutoRedeemItemBlacklist = [200];

        // Dalamud saves only base sections plus profile patches. Loading this file
        // again must not replace the live, character-specific configuration.
        Configuration saved = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(_configuration))!;
        Assert.Equal(baseEnabled, saved.Advanced.AutoRedeemRewardItems);
        Assert.Equal(baseEnabled, saved.Advanced.NeverFly);
        Assert.Contains(100u, saved.Advanced.AutoRedeemItemBlacklist);

        AssertRuntimeSettings(!baseEnabled, 200);
        _configuration.ActivateForCharacter("99");
        AssertRuntimeSettings(baseEnabled, 100);
        _configuration.ActivateForCharacter("42");
        AssertRuntimeSettings(!baseEnabled, 200);
        _configuration.ClearActiveProfile();
        AssertRuntimeSettings(baseEnabled, 100);
    }

    [Fact]
    public void Redemption_ProfileDisabled_DoesNotEnumerateInventory()
    {
        _configuration.Advanced.AutoRedeemRewardItems = true;
        _configuration.CreateProfile("Alt");
        _configuration.AssignProfile("42", "Alt");
        _configuration.ActivateForCharacter("42");
        _configuration.Advanced.AutoRedeemRewardItems = false;
        _ = JsonConvert.SerializeObject(_configuration);

        // Exercises the actual redemption entry point: disabled means it must
        // return before consulting quest data, item data, or native inventory.
        Assert.Empty(RedeemRewardItems.CreateRedeemTasks(null!, null!));
    }

    [Fact]
    public void RuntimeLookup_SeesUnsavedEditsWithoutReplacingTheInstance()
    {
        _configuration.CreateProfile("Alt");
        _configuration.ActivateProfile("Alt");
        _configuration.Advanced.AutoRedeemItemBlacklist.Add(200);

        Assert.Same(_configuration, Configuration.Instance());
        Assert.Contains(200u, Configuration.Instance().Advanced.AutoRedeemItemBlacklist);
        Assert.DoesNotContain(200u, _configuration.BaseAdvanced.AutoRedeemItemBlacklist);
    }

    [Fact]
    public void Shutdown_DropsRuntimeBinding()
    {
        Configuration.ClearRuntime(_configuration);

        Assert.Throws<InvalidOperationException>(() => Configuration.Instance());
    }

    [Fact]
    public void OldInstanceCleanup_DoesNotClearReplacement()
    {
        Configuration replacement = new();
        Configuration.InitializeRuntime(replacement);
        try
        {
            Configuration.ClearRuntime(_configuration);
            Assert.Same(replacement, Configuration.Instance());
        }
        finally
        {
            Configuration.ClearRuntime(replacement);
        }
    }

    private void AssertRuntimeSettings(bool enabled, uint blockedItem)
    {
        Configuration actual = Configuration.Instance();
        Assert.Same(_configuration, actual);
        Assert.Equal(enabled, actual.Advanced.AutoRedeemRewardItems);
        Assert.Equal(enabled, actual.Advanced.NeverFly);
        Assert.Equal(new[] { blockedItem }, actual.Advanced.AutoRedeemItemBlacklist);
    }

    public void Dispose() => Configuration.ClearRuntime(_configuration);
}
