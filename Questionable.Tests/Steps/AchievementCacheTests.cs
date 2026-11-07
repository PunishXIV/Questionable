using Questionable.Functions;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class AchievementCacheTests
{
    private readonly HashSet<int> _complete = [1580];
    private string? _contentId = "1";
    private bool _loaded;
    private int _shown;
    private int _hidden;
    private readonly AchievementCache _cache;

    public AchievementCacheTests() =>
        _cache = new(() => _contentId, () => _loaded, _complete.Contains, () => _shown++, () => _hidden++);

    [Fact]
    public void Window_OpensOnceClosesOnData()
    {
        Assert.False(_cache.IsComplete(1580));
        Assert.False(_cache.IsComplete(1580));
        Assert.Equal(1, _shown);

        _loaded = true;
        Assert.True(_cache.IsComplete(1580));
        Assert.Equal(1, _hidden);
    }

    [Fact]
    public void RemembersCompletionsAfterTheDataUnloads()
    {
        _loaded = true;
        Assert.True(_cache.IsComplete(1580));
        Assert.False(_cache.IsComplete(2819));

        _loaded = false;
        Assert.True(_cache.IsComplete(1580));
        Assert.False(_cache.IsComplete(2819));
        Assert.Equal(0, _shown);
    }

    [Fact]
    public void ANewLoginStartsOver()
    {
        _loaded = true;
        Assert.True(_cache.IsComplete(1580));

        _loaded = false;
        _contentId = "2";
        Assert.False(_cache.IsComplete(1580));
        Assert.Equal(1, _shown);
    }

    [Fact]
    public void AllRequestedAchievementsMustBeComplete()
    {
        _loaded = true;
        Assert.False(_cache.IsComplete(1580, 2819));
        _complete.Add(2819);
        Assert.True(_cache.IsComplete(1580, 2819));
    }
}
