using Questionable.Windows.JournalComponents;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class JournalRefreshTests
{
    private static readonly DateTime Start = new(2026, 11, 9, 12, 0, 0);

    [Fact]
    public void FirstTick_RecountsAtOnce()
    {
        int recounts = 0;
        new JournalRefresh(TimeSpan.FromSeconds(2)).Tick(Start, () => ++recounts > 0);
        Assert.Equal(1, recounts);
    }

    [Fact]
    public void Tick_WaitsForTheInterval()
    {
        JournalRefresh refresh = new(TimeSpan.FromSeconds(2));
        int recounts = 0;
        bool Recount() => ++recounts > 0;

        refresh.Tick(Start, Recount);
        refresh.Tick(Start.AddSeconds(1.9), Recount);
        Assert.Equal(1, recounts);
        refresh.Tick(Start.AddSeconds(2), Recount);
        Assert.Equal(2, recounts);
    }

    [Fact]
    public void Tick_PassesOnWhetherCountsChanged()
    {
        JournalRefresh refresh = new(TimeSpan.FromSeconds(2));
        Assert.False(refresh.Tick(Start, () => false));
        Assert.True(refresh.Tick(Start.AddSeconds(2), () => true));
        Assert.False(refresh.Tick(Start.AddSeconds(3), () => true));
    }
}
