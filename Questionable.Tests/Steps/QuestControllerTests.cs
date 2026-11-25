using Questionable.Controller;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestControllerTests
{
    [Fact]
    public void StopKind_NamesProblemOrFinished()
    {
        Assert.Equal("Stuck", QuestController.StopKindName(EProblemKind.Stuck, finished: false));
        Assert.Equal("Finished", QuestController.StopKindName(null, finished: true));
        Assert.Equal("", QuestController.StopKindName(null, finished: false));
    }

    [Fact]
    public void AcceptStall_CountsFromTheAcceptWait()
    {
        DateTime step = new(2026, 11, 22, 17, 50, 7);
        DateTime wait = new(2026, 11, 22, 17, 50, 42);
        Assert.Equal(wait, QuestController.AcceptStallStart(step, wait));
        Assert.Equal(step, QuestController.AcceptStallStart(step, null));
        Assert.Equal(step, QuestController.AcceptStallStart(step, step.AddSeconds(-1)));
    }
}
