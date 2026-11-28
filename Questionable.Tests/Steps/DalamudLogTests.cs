using Questionable.Utils;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class DalamudLogTests
{
    private static readonly string Root = Path.Combine("C:", "XIVLauncher");

    [Fact]
    public void LauncherFolder_ComesFirst() =>
        Assert.Equal(Root, DalamudLog.FindFolder(Root, _ => true));

    [Fact]
    public void LogsFolder_IsTheFallback() =>
        Assert.Equal(Path.Combine(Root, "logs"),
            DalamudLog.FindFolder(Root, x => x == Path.Combine(Root, "logs", "dalamud.log")));

    [Fact]
    public void NoLog_GivesNull() => Assert.Null(DalamudLog.FindFolder(Root, _ => false));
}
