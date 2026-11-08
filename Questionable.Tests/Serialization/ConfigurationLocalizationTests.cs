using Questionable.Controller;
using Questionable.Windows.ConfigComponents;
using Xunit;

namespace Questionable.Tests.Serialization;

public sealed class ConfigurationLocalizationTests
{
    [Fact]
    public void ProfileDuringLoad_AppliesLatestLanguage()
    {
        Configuration configuration = new();
        AddProfile(configuration, "Alt", "ja-jp");
        List<string> applied = [];
        bool applying = false;

        using ConfigurationLocalization localization = new(configuration, language =>
        {
            Assert.False(applying);
            applying = true;
            if (language == "en")
                configuration.ActivateProfile("Alt");
            applied.Add(language);
            applying = false;
        });

        Assert.Equal(["en", "ja-jp"], applied);
        Assert.Equal(configuration.General.Language, applied[^1]);
    }

    [Fact]
    public void InitialLoadFailure_UnsubscribesListener()
    {
        Configuration configuration = new();
        int attempts = 0;
        InvalidOperationException failure = new("Localization failed");

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            new ConfigurationLocalization(configuration, _ =>
            {
                attempts++;
                throw failure;
            })));

        configuration.SetLanguage("ja-jp");
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ConcurrentChange_AppliesLatestLanguage()
    {
        Configuration configuration = new();
        using ManualResetEventSlim firstLoadStarted = new();
        using ManualResetEventSlim releaseFirstLoad = new();
        using ManualResetEventSlim secondChangeObserved = new();
        configuration.LanguageChanged += language =>
        {
            if (language == "zh-cn")
                secondChangeObserved.Set();
        };
        List<string> applied = [];
        int activeLoads = 0;
        using ConfigurationLocalization localization = new(configuration, language =>
        {
            Assert.Equal(1, Interlocked.Increment(ref activeLoads));
            try
            {
                if (language == "ja-jp")
                {
                    firstLoadStarted.Set();
                    Assert.True(releaseFirstLoad.Wait(TimeSpan.FromSeconds(10)));
                }

                applied.Add(language);
            }
            finally
            {
                Interlocked.Decrement(ref activeLoads);
            }
        });

        Task firstChange = Task.Run(() => configuration.SetLanguage("ja-jp"));
        Task secondChange = Task.CompletedTask;
        try
        {
            Assert.True(firstLoadStarted.Wait(TimeSpan.FromSeconds(10)));
            secondChange = Task.Run(() => configuration.SetLanguage("zh-cn"));
            Assert.True(secondChangeObserved.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, Volatile.Read(ref activeLoads));
        }
        finally
        {
            releaseFirstLoad.Set();
            await Task.WhenAll(firstChange, secondChange);
        }

        Assert.Equal(["en", "ja-jp", "zh-cn"], applied);
    }

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
        // ConfigWindow.OnClose restores the current character's profile through this operation.
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
    public void SameLanguage_DoesNotReload()
    {
        Configuration configuration = new();
        AddProfile(configuration, "First", "ja-jp");
        AddProfile(configuration, "Second", "ja-jp");
        configuration.ActivateProfile("First");
        List<string> applied = [];
        using ConfigurationLocalization localization = new(configuration, applied.Add);

        configuration.ActivateProfile("Second");
        configuration.SetLanguage("ja-jp");
        configuration.ActivateProfile("Second");

        Assert.Equal(["ja-jp"], applied);
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

    [Fact]
    public void Dispose_UnsubscribesFromChanges()
    {
        Configuration configuration = new();
        AddProfile(configuration, "Alt", "ja-jp");
        List<string> applied = [];
        ConfigurationLocalization localization = new(configuration, applied.Add);

        localization.Dispose();
        localization.Dispose();
        configuration.SetLanguage("zh-cn");
        configuration.ActivateProfile("Alt");
        configuration.ClearActiveProfile();

        Assert.Equal(["en"], applied);
    }

    private static void AddProfile(Configuration configuration, string name, string language)
    {
        configuration.CreateProfile(name);
        configuration.ActivateProfile(name);
        configuration.SetLanguage(language);
        configuration.ClearActiveProfile();
    }
}

public sealed class ConfigComponentTests
{
    private const string Saved = "qst:duty:KzE7KzI7LTM=";

    [Fact]
    public void Export_KeepsSavedFormat()
    {
        Assert.Equal(Saved, ConfigComponent.ExportCfcIds("qst:duty:", [1, 2], [3]));
    }

    [Fact]
    public void Decode_ReadsSavedFormat()
    {
        Assert.Equal("+1;+2;-3", ConfigComponent.DecodeCfcIds(Saved, "qst:duty:"));
    }

    [Fact]
    public void Decode_BadBase64_ReturnsNull()
    {
        Assert.Null(ConfigComponent.DecodeCfcIds("qst:duty:!!!", "qst:duty:"));
    }

    [Fact]
    public void Decode_OtherPrefix_ReturnsNull()
    {
        Assert.Null(ConfigComponent.DecodeCfcIds(Saved, "qst:single:"));
    }

    [Fact]
    public void Import_ReplacesBothLists()
    {
        HashSet<uint> whitelisted = [7];
        HashSet<uint> blacklisted = [8];

        ConfigComponent.ImportCfcIds("+1;+2;-3", whitelisted, blacklisted);

        Assert.Equal([1u, 2u], whitelisted);
        Assert.Equal([3u], blacklisted);
    }

    [Fact]
    public void Import_SkipsUnreadableParts()
    {
        HashSet<uint> whitelisted = [];
        HashSet<uint> blacklisted = [];

        ConfigComponent.ImportCfcIds("+abc;-;+4;x5;-6;;+4294967296", whitelisted, blacklisted);

        Assert.Equal([4u], whitelisted);
        Assert.Equal([6u], blacklisted);
    }
}
