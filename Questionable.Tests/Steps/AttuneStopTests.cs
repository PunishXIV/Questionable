using System.Reflection;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Model.Common;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class AttuneStopTests
{
    private static bool Complete(ITask task, bool armed)
    {
        QuestController controller = TestQuestController.Create();
        controller.StopAfterAttune = armed;
        typeof(QuestController).GetMethod("OnTaskComplete", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [task]);
        return controller.AttuneStopDue;
    }

    [Fact]
    public void FinishedAttune_StopsWhenArmed()
    {
        Aetheryte.Attune attune = new(EAetheryteLocation.Limsa, null);
        Assert.True(Complete(attune, armed: true));
        Assert.False(Complete(attune, armed: false));
    }

    [Fact]
    public void ShardAttune_DoesNotStop() =>
        Assert.False(Complete(new AethernetShard.Attune(EAetheryteLocation.Limsa), armed: true));
}
