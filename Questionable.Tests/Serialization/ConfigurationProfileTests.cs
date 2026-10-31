// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Questionable.Tests.Serialization;

public sealed class ConfigurationProfileTests
{
    [Fact]
    public void EditUnderProfile_IsStoredAsSparsePatch_AndLeavesBaseAlone()
    {
        Configuration configuration = new();
        configuration.CreateProfile("Alt");
        configuration.ActivateProfile("Alt");

        configuration.General.MountId = 71;
        configuration.CaptureActiveProfile();

        JObject patch = configuration.Profiles["Alt"]["General"];
        Assert.Equal(71u, patch["MountId"]!.Value<uint>());
        Assert.Single(patch.Properties());
        Assert.Equal(1u, configuration.BaseGeneral.MountId);
    }

    [Fact]
    public void EditUnderProfile_OfUntouchedSection_DoesNotChangeBase()
    {
        Configuration configuration = new();
        configuration.CreateProfile("Alt");
        configuration.ActivateProfile("Alt");

        configuration.Stop.Enabled = true;
        configuration.Duties.WhitelistedDutyCfcIds.Add(1234);
        configuration.ClearActiveProfile();

        Assert.False(configuration.Stop.Enabled);
        Assert.Empty(configuration.Duties.WhitelistedDutyCfcIds);
        Assert.True(configuration.Profiles["Alt"]["Stop"]["Enabled"]!.Value<bool>());
    }

    [Fact]
    public void ActivatingProfile_AppliesPatch_IncludingCollections()
    {
        Configuration configuration = new();
        configuration.CreateProfile("Alt");
        configuration.ActivateProfile("Alt");
        configuration.Duties.WhitelistedDutyCfcIds.Add(1234);
        configuration.ClearActiveProfile();

        configuration.ActivateProfile("Alt");

        Assert.Contains(1234u, configuration.Duties.WhitelistedDutyCfcIds);
    }

    [Fact]
    public void ValueChangedBackToBase_IsDroppedFromPatch()
    {
        Configuration configuration = new();
        configuration.CreateProfile("Alt");
        configuration.ActivateProfile("Alt");
        configuration.General.MountId = 71;
        configuration.CaptureActiveProfile();

        configuration.General.MountId = 1;
        configuration.CaptureActiveProfile();

        Assert.False(configuration.Profiles["Alt"].ContainsKey("General"));
    }

    [Fact]
    public void Serializing_CapturesActiveProfileEdits()
    {
        Configuration configuration = new();
        configuration.CreateProfile("Alt");
        configuration.ActivateProfile("Alt");
        configuration.General.MountId = 71;

        Configuration roundTripped = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(configuration))!;

        Assert.Equal(1u, roundTripped.General.MountId);
        roundTripped.ActivateProfile("Alt");
        Assert.Equal(71u, roundTripped.General.MountId);
    }

    [Fact]
    public void CharacterBinding_FollowsRenameAndDelete()
    {
        Configuration configuration = new();
        configuration.CreateProfile("Alt");
        configuration.AssignProfile("42", "Alt");

        Assert.True(configuration.RenameProfile("Alt", "Crafter"));
        Assert.Equal("Crafter", configuration.GetProfileForCharacter("42"));

        configuration.DeleteProfile("Crafter");
        Assert.Null(configuration.GetProfileForCharacter("42"));
        Assert.Empty(configuration.CharacterProfiles);
    }

    [Fact]
    public void ActivateForCharacter_WithoutBinding_UsesBase()
    {
        Configuration configuration = new();
        configuration.CreateProfile("Alt");
        configuration.ActivateProfile("Alt");

        configuration.ActivateForCharacter("42");

        Assert.Null(configuration.ActiveProfileName);
    }
}
