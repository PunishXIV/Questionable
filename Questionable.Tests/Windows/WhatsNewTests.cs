using Questionable.Windows;
using Xunit;

namespace Questionable.Tests.Windows;

public sealed class WhatsNewTests
{
    private static readonly WhatsNew.Entry Old = Entry(new Version(16, 0, 0, 0));
    private static readonly WhatsNew.Entry New = Entry(new Version(16, 1, 0, 0));

    private static WhatsNew.Entry Entry(Version version) => new(version, () => "title", () => "text");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a version")]
    public void NoSavedVersion_ShowsAll(string? seen) =>
        Assert.Equal(new[] { Old, New }, WhatsNew.Unseen([Old, New], seen));

    [Fact]
    public void SeenVersion_ShowsOnlyNewer() =>
        Assert.Equal(new[] { New }, WhatsNew.Unseen([Old, New], "16.0.0.0"));

    [Fact]
    public void LatestSeen_ShowsNone() =>
        Assert.Empty(WhatsNew.Unseen([Old, New], "16.1.0.0"));

    [Fact]
    public void Entries_NotNewerThanPlugin()
    {
        Version current = Version.Parse(WhatsNew.Current);
        Assert.All(WhatsNew.Entries, x => Assert.True(x.Version <= current, $"{x.Version} is newer than {current}"));
    }

    [Fact]
    public void Toggles_ChangeTheirSetting()
    {
        foreach (WhatsNew.Toggle toggle in WhatsNew.Entries.Select(x => x.Toggle).OfType<WhatsNew.Toggle>())
        {
            var configuration = new Configuration();
            toggle.Set(configuration, true);
            Assert.True(toggle.Get(configuration));
            toggle.Set(configuration, false);
            Assert.False(toggle.Get(configuration));
        }
    }
}
