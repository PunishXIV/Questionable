using System.Reflection;
using System.Runtime.CompilerServices;
using NSubstitute;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Common;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class TeleportTaskDetectorTests
{
    private static readonly AetheryteShortcut.Task Shortcut = new(null, null, EAetheryteLocation.Limsa, 129);

    [Fact]
    public void StartedShortcut_IsUpcoming() =>
        Assert.Same(Shortcut, TeleportTaskDetector.GetUpcoming(StartedShortcut(cast: false), null));

    [Fact]
    public void CastShortcut_IsNotUpcoming() =>
        Assert.Null(TeleportTaskDetector.GetUpcoming(StartedShortcut(cast: true), null));

    [Fact]
    public void QueuedShortcut_IsNotUpcoming() =>
        Assert.Null(TeleportTaskDetector.GetUpcoming(null, Shortcut));

    [Fact]
    public void QueuedItem_IsUpcoming()
    {
        UseItem.IUseTask item = Substitute.For<UseItem.IUseTask>();
        Assert.Same(item, TeleportTaskDetector.GetUpcoming(null, item));
    }

    [Fact]
    public void ItemBehindRunningTask_IsNotUpcoming() =>
        Assert.Null(TeleportTaskDetector.GetUpcoming(Substitute.For<ITaskExecutor>(), Substitute.For<UseItem.IUseTask>()));

    [Fact]
    public void OtherTask_IsNotUpcoming() =>
        Assert.Null(TeleportTaskDetector.GetUpcoming(null, Substitute.For<ITask>()));

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
