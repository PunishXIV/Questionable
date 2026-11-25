using Microsoft.Extensions.Logging.Abstractions;
using Questionable.Controller.Steps;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class TaskQueueTests
{
    [Fact]
    public void Cancel_ClearsExecutorThenStopsOnce()
    {
        TaskQueue queue = new(NullLogger.Instance);
        Executor executor = new() { OnStop = () => Assert.Null(queue.CurrentTaskExecutor) };
        Assert.True(queue.StartTask(executor, new TestTask()));

        Assert.True(queue.CancelCurrentTask());
        Assert.True(queue.CancelCurrentTask());
        Assert.Equal(1, executor.StopCount);
    }

    [Fact]
    public void Reset_ClearsQueueIfStopThrows()
    {
        TaskQueue queue = new(NullLogger.Instance);
        Executor executor = new() { OnStop = () => throw new InvalidOperationException("Provider unloaded") };
        queue.StartTask(executor, new TestTask());
        queue.Enqueue(new TestTask());

        Assert.False(queue.Reset());
        Assert.True(queue.AllTasksComplete);
        Assert.Empty(queue.RemainingTasks);
        Assert.True(queue.Reset());
        Assert.Equal(1, executor.StopCount);
    }

    [Fact]
    public void Interruption_CancelsBeforeReplay()
    {
        TaskQueue queue = new(NullLogger.Instance);
        TestTask current = new("current");
        TestTask pending = new("pending");
        TestTask combat = new("combat");
        Executor executor = new();
        queue.Enqueue(current);
        queue.Enqueue(pending);
        Assert.True(queue.TryDequeue(out ITask? task));
        queue.StartTask(executor, task);

        Assert.True(queue.InterruptWith([combat]));
        Assert.Equal(1, executor.StopCount);
        Assert.Null(queue.CurrentTaskExecutor);
        Assert.Equal(new ITask[] { combat, current, pending }, queue.RemainingTasks);
    }

    [Fact]
    public void FailedCancellation_StartsNoInterruption()
    {
        TaskQueue queue = new(NullLogger.Instance);
        Executor executor = new() { OnStop = () => throw new InvalidOperationException("Stop rejected") };
        queue.StartTask(executor, new TestTask());
        queue.Enqueue(new TestTask());

        Assert.False(queue.InterruptWith([new TestTask()]));
        Assert.True(queue.AllTasksComplete);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Interruption_ReplaysRedoableThenRest(bool hasCurrent, bool redoCurrent)
    {
        TaskQueue queue = new(NullLogger.Instance);
        TestTask first = new("first");
        TestTask second = new("second");
        foreach (TestTask completed in new[] { first, new TestTask("once", false), second })
        {
            queue.Enqueue(completed);
            Assert.True(queue.TryDequeue(out _));
        }

        TestTask current = new("current", redoCurrent);
        Executor executor = new();
        if (hasCurrent)
        {
            queue.Enqueue(current);
            Assert.True(queue.TryDequeue(out ITask? task));
            Assert.True(queue.StartTask(executor, task));
        }
        TestTask pending = new("pending");
        TestTask interruption = new("interruption");
        queue.Enqueue(pending);

        Assert.True(queue.InterruptWith([interruption]));

        List<ITask> expected = [interruption, first, second];
        if (hasCurrent)
            expected.Add(current);
        expected.Add(pending);
        Assert.Equal(expected, queue.RemainingTasks);
        Assert.Null(queue.CurrentTaskExecutor);
        Assert.Equal(hasCurrent ? 1 : 0, executor.StopCount);
    }

    [Fact]
    public void FailedStartup_RollsBackKeepsError()
    {
        TaskQueue queue = new(NullLogger.Instance);
        InvalidOperationException startupFailure = new("Preset setup failed after enabling provider");
        Executor executor = new()
        {
            OnStart = () => throw startupFailure,
            OnStop = () => throw new InvalidOperationException("Provider disappeared during rollback")
        };

        Assert.Same(startupFailure,
            Assert.Throws<InvalidOperationException>(() => queue.StartTask(executor, new TestTask())));
        Assert.Null(queue.CurrentTaskExecutor);
        Assert.Equal(1, executor.StopCount);
    }

    [Fact]
    public void SkippedStart_KeepsProviderWork()
    {
        TaskQueue queue = new(NullLogger.Instance);
        Executor executor = new() { OnStart = () => false };

        Assert.False(queue.StartTask(executor, new TestTask()));
        Assert.True(queue.AllTasksComplete);
        Assert.Equal(0, executor.StopCount);
    }

    [Fact]
    public void Completion_DoesNotCancelProvider()
    {
        TaskQueue queue = new(NullLogger.Instance);
        Executor executor = new();
        queue.StartTask(executor, new TestTask());
        queue.CurrentTaskExecutor = null;

        Assert.True(queue.Reset());
        Assert.Equal(0, executor.StopCount);
    }

    private sealed record TestTask(string Name = "task", bool Redo = true) : ITask
    {
        public bool ShouldRedoOnInterrupt() => Redo;
    }

    private sealed class Executor : TaskExecutor<TestTask>, ITaskExecutor.IStoppable
    {
        public Func<bool> OnStart { get; init; } = () => true;
        public Action OnStop { get; init; } = () => { };
        public int StopCount { get; private set; }
        protected override bool Start() => OnStart();
        public override ETaskResult Update() => ETaskResult.TaskComplete;
        public override bool ShouldInterruptOnDamage() => true;
        public void StopNow()
        {
            ++StopCount;
            OnStop();
        }
    }
}
