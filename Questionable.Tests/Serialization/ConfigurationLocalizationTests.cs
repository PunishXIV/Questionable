using Questionable.Controller;
using Xunit;

namespace Questionable.Tests.Serialization;

public sealed class ConfigurationLocalizationTests
{
    [Fact]
    public void Subscribe_AppliesActiveProfileLanguage()
    {
        Configuration configuration = new();
        AddProfile(configuration, "Alt", "ja-jp");
        configuration.ActivateProfile("Alt");
        List<string> applied = [];

        using ConfigurationLocalization localization = new(configuration, applied.Add);

        Assert.Equal(["ja-jp"], applied);
    }

    [Theory]
    [InlineData(false, "en")]
    [InlineData(true, "zh-cn")]
    public void EditThenRestore_AppliesBothLanguages(bool hasProfile, string characterLanguage)
    {
        Configuration configuration = new();
        AddProfile(configuration, "Character", characterLanguage);
        if (hasProfile)
            configuration.AssignProfile("42", "Character");
        configuration.ActivateForCharacter("42");
        configuration.CreateProfile("Editing");
        List<string> applied = [];
        using ConfigurationLocalization localization = new(configuration, language =>
        {
            Assert.Equal(configuration.General.Language, language);
            applied.Add(language);
        });

        configuration.ActivateProfile("Editing");
        configuration.SetLanguage("ja-jp");
        configuration.ActivateForCharacter("42");

        Assert.Equal(characterLanguage, applied[^1]);
        Assert.Contains("ja-jp", applied);
        configuration.ActivateProfile("Editing");
        Assert.Equal("ja-jp", applied[^1]);
        Assert.Equal("en", configuration.BaseGeneral.Language);
    }

    [Fact]
    public void LoginLogoutSwitch_AppliesEachLanguage()
    {
        Configuration configuration = new();
        AddProfile(configuration, "First", "ja-jp");
        AddProfile(configuration, "Second", "zh-cn");
        configuration.AssignProfile("42", "First");
        configuration.AssignProfile("43", "Second");
        List<string> applied = [];
        using ConfigurationLocalization localization = new(configuration, applied.Add);

        configuration.ActivateForCharacter("42");
        configuration.ClearActiveProfile();
        configuration.ActivateForCharacter("43");
        configuration.ActivateForCharacter("unbound");

        Assert.Equal(["en", "ja-jp", "en", "zh-cn", "en"], applied);
    }

    [Fact]
    public void DeletedProfile_RestoresDefaultLanguage()
    {
        Configuration configuration = new();
        AddProfile(configuration, "Alt", "ja-jp");
        configuration.ActivateProfile("Alt");
        List<string> applied = [];
        using ConfigurationLocalization localization = new(configuration, applied.Add);

        configuration.DeleteProfile("Alt");

        Assert.Equal(["ja-jp", "en"], applied);
    }

    [Fact]
    public void DefaultLanguageEdit_AppliesAndPersists()
    {
        Configuration configuration = new();
        AddProfile(configuration, "Alt", "ja-jp");
        List<string> applied = [];
        using ConfigurationLocalization localization = new(configuration, applied.Add);

        configuration.SetLanguage("zh-cn");
        configuration.ActivateProfile("Alt");
        configuration.ClearActiveProfile();

        Assert.Equal(["en", "zh-cn", "ja-jp", "zh-cn"], applied);
    }

    private static void AddProfile(Configuration configuration, string name, string language)
    {
        configuration.CreateProfile(name);
        configuration.ActivateProfile(name);
        configuration.SetLanguage(language);
        configuration.ClearActiveProfile();
    }
}
