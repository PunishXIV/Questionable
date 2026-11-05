using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using static Questionable.Controller.Steps.ITaskExecutor;

namespace Questionable.Tests.Steps;

internal sealed class TaskRunnerFixture : IDisposable
{
    private readonly ServiceProvider? _ownedServices;
    public TestInterruptionSource Interruptions { get; } = new();
    public ICondition Condition { get; }
    public IChatGui Chat { get; } = Substitute.For<IChatGui>();
    public List<string> Notifications { get; } = [];
    public RecordingLogger Log { get; } = new();
    public Runner Controller { get; }
    public TaskQueue Queue => Controller.Queue;

    public TaskRunnerFixture(ICondition? condition = null, IServiceProvider? provider = null, TaskToastClassifier? toasts = null)
    {
        Condition = condition ?? Substitute.For<ICondition>();
        if (provider == null)
            provider = _ownedServices = new ServiceCollection().BuildServiceProvider();
        Controller = new(Chat, Condition, provider, Interruptions, toasts ?? CreateToasts(), Notifications.Add, Log);
    }

    public static TaskToastClassifier CreateToasts() => new("Event canceled.",
        new("^Action canceled\\. You are under attack\\.$"), "Unable to execute command while suffering status affliction.");

    public void ReceiveDamage() => Interruptions.Raise();

    public void Dispose()
    {
        Controller.Dispose();
        _ownedServices?.Dispose();
    }

    internal sealed class Runner(IChatGui chat, ICondition condition, IServiceProvider services,
        ITaskInterruptionSource interruptions, TaskToastClassifier toasts, Action<string> notify,
        ILogger<Runner> logger) : MiniTaskController<Runner>(chat, condition, services, interruptions, toasts, notify, logger)
    {
        public TaskQueue Queue => _taskQueue;
        public bool AllowNewTasks { get; set; } = true;
        public int CompletedTasks { get; private set; }
        public List<string> Stops { get; } = [];
        public List<(Exception Error, ITask Task)> Failures { get; } = [];
        public void Tick() => UpdateCurrentTask();
        protected override bool CanStartNextTask() => AllowNewTasks;
        protected override void OnTaskComplete(ITask task) => ++CompletedTasks;
        protected override void OnTaskFailure(Exception exception, ITask task) => Failures.Add((exception, task));
        public override void Stop(string label)
        {
            Stops.Add(label);
            _taskQueue.Reset();
        }
    }

    internal sealed class TestInterruptionSource : ITaskInterruptionSource
    {
        private EventHandler? _handlers;
        public bool ThrowAfterAdding { get; set; }
        public int AddCalls { get; private set; }
        public int RemoveCalls { get; private set; }
        public int SubscriberCount => _handlers?.GetInvocationList().Length ?? 0;
        public event EventHandler? Interrupted
        {
            add
            {
                AddCalls++;
                _handlers += value;
                if (ThrowAfterAdding)
                    throw new InvalidOperationException("subscription failed after adding handler");
            }
            remove
            {
                RemoveCalls++;
                _handlers -= value;
            }
        }
        public void Raise() => _handlers?.Invoke(this, EventArgs.Empty);
    }

    internal sealed class RecordingLogger : ILogger<Runner>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception, formatter(state, exception)));
    }
}
