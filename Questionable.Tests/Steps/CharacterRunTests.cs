using Questionable.Controller;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CharacterRunTests
{
    private static readonly AutoRetainerIpc.Character[] Characters =
    [
        new() { ContentId = 1, Name = "A", World = "W" },
        new() { ContentId = 2, Name = "B", World = "W" },
        new() { ContentId = 3, Name = "C", World = "W" },
        new() { ContentId = 4, Name = "D", World = "W" },
    ];

    private static IEnumerable<ulong> Queue(ulong[] ticked, ulong? current) =>
        CharacterRun.GetQueue(Characters, ticked, current).Select(x => x.ContentId);

    [Fact]
    public void Queue_StartsWithCurrent() => Assert.Equal([3UL, 4UL, 1UL], Queue([1, 3, 4], 3));

    [Fact]
    public void Queue_KeepsListOrder() => Assert.Equal([1UL, 2UL, 4UL], Queue([4, 2, 1], 1));

    [Theory]
    [InlineData(2UL)]
    [InlineData(null)]
    public void Queue_CurrentNotTicked(ulong? current) => Assert.Equal([1UL, 3UL], Queue([1, 3], current));

    [Fact]
    public void Queue_IgnoresUnknownIds() => Assert.Equal([2UL], Queue([2, 99], 99));

    [Fact]
    public void Queue_EmptyWithoutTicks() => Assert.Empty(Queue([], 1));

    [Theory]
    [InlineData(true, 0, 3, 1)]
    [InlineData(true, 1, 3, 2)]
    [InlineData(true, 2, 3, null)]
    [InlineData(false, 0, 3, null)]
    public void Next_NeedsContinueAndMore(bool canContinue, int index, int count, int? expected) =>
        Assert.Equal(expected, CharacterRun.NextIndex(canContinue, index, count));
}
