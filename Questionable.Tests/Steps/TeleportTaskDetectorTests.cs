using System.Reflection;
using System.Runtime.CompilerServices;
using NSubstitute;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class TeleportTaskDetectorTests
{
    private const uint Here = 129;
    private static readonly AetheryteShortcut.Task Shortcut = new(null, null, EAetheryteLocation.Limsa, 129);

    [Fact]
    public void StartedShortcut_IsUpcoming() =>
        Assert.Same(Shortcut, TeleportTaskDetector.GetUpcoming(StartedShortcut(cast: false), null, Here));

    [Fact]
    public void CastShortcut_IsNotUpcoming() =>
        Assert.Null(TeleportTaskDetector.GetUpcoming(StartedShortcut(cast: true), null, Here));

    [Fact]
    public void QueuedShortcut_IsNotUpcoming() =>
        Assert.Null(TeleportTaskDetector.GetUpcoming(null, Shortcut, Here));

    [Fact]
    public void TravelToOtherZone_IsUpcoming()
    {
        Travel.Task travel = new(132, null, null);
        Assert.Same(travel, TeleportTaskDetector.GetUpcoming(null, travel, Here));
    }

    [Fact]
    public void TravelInThisZone_IsNotUpcoming() =>
        Assert.Null(TeleportTaskDetector.GetUpcoming(null, new Travel.Task(Here, System.Numerics.Vector3.One, null), Here));

    [Fact]
    public void QueuedTicket_IsUpcoming()
    {
        UseItem.IUseTask ticket = Item(QuestStep.VesperBayAetheryteTicket);
        Assert.Same(ticket, TeleportTaskDetector.GetUpcoming(null, ticket, Here));
    }

    [Fact]
    public void QueuedQuestItem_IsNotUpcoming() =>
        Assert.Null(TeleportTaskDetector.GetUpcoming(null, Item(2001234), Here));

    [Fact]
    public void TicketBehindRunningTask_NotUpcoming() =>
        Assert.Null(TeleportTaskDetector.GetUpcoming(Substitute.For<ITaskExecutor>(), Item(QuestStep.VesperBayAetheryteTicket), Here));

    [Fact]
    public void OtherTask_IsNotUpcoming() =>
        Assert.Null(TeleportTaskDetector.GetUpcoming(null, Substitute.For<ITask>(), Here));

    private static UseItem.IUseTask Item(uint itemId)
    {
        UseItem.IUseTask item = Substitute.For<UseItem.IUseTask>();
        item.ItemId.Returns(itemId);
        return item;
    }

    private static AetheryteShortcut.Executor StartedShortcut(bool cast)
    {
        var executor = (AetheryteShortcut.Executor)RuntimeHelpers.GetUninitializedObject(typeof(AetheryteShortcut.Executor));
        typeof(TaskExecutor<AetheryteShortcut.Task>).GetProperty("Task", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(executor, Shortcut);
        typeof(AetheryteShortcut.Executor).GetField("_teleported", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(executor, cast);
        return executor;
    }
}
