using Questionable.Controller;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

[Collection(nameof(SetAsideQuestsTests))]
public sealed class SetAsideQuestsTests : IDisposable
{
    [Fact]
    public void SetAside_OnlyAtThatSequence()
    {
        SetAsideQuests.Add(new QuestId(1234), 3);

        Assert.True(SetAsideQuests.Contains(new QuestId(1234), 3));
        Assert.False(SetAsideQuests.Contains(new QuestId(1234), 4));
        Assert.False(SetAsideQuests.Contains(new QuestId(4321), 3));
        Assert.False(SetAsideQuests.Contains(null, 3));
    }

    [Fact]
    public void Prune_DropsQuestsThatMovedOn()
    {
        SetAsideQuests.Add(new QuestId(1234), 3);
        SetAsideQuests.Add(new QuestId(4321), 2);

        Assert.Equal(1, SetAsideQuests.Prune(x => x.Value == 1234 ? (byte)3 : (byte)255));
        Assert.True(SetAsideQuests.Contains(new QuestId(1234), 3));
    }

    public void Dispose() => SetAsideQuests.Clear();
}
