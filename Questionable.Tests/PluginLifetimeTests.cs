using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA2007 // Test awaits retain xUnit's synchronization context.

namespace Questionable.Tests;

public sealed class PluginLifetimeTests
{
    [Fact]
    public async Task LateInitializationFailureUnhooksAndDisposesPreviouslyCreatedServices()
    {
        List<string> events = [];
        List<Exception> cleanupErrors = [];
        var failure = new InvalidOperationException("late initialization failed");
        var lifetime = new PluginLifetime();
        EventSource? eventSource = null;
        AsyncService? updater = null;
        int failedConstructions = 0;
        ServiceCollection registrations = [];
        registrations.AddSingleton(_ => new EventSource(events));
        registrations.AddSingleton(_ => new AsyncService(events));
        registrations.AddSingleton<FailedInitializer>(_ =>
        {
            failedConstructions++;
            throw failure;
        });

        Exception observed = await Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.InitializeAsync(() =>
        {
            var services = registrations.BuildServiceProvider();
            lifetime.OwnServices(services);
            eventSource = services.GetRequiredService<EventSource>();
            updater = services.GetRequiredService<AsyncService>();
            services.GetRequiredService<FailedInitializer>();
            return Task.CompletedTask;
        }, () =>
        {
            eventSource?.Stop();
            return ValueTask.CompletedTask;
        }, () => events.Add("globals"), cleanupErrors.Add, CancellationToken.None));

        Assert.Same(failure, observed);
        Assert.Equal(1, failedConstructions);
        Assert.NotNull(eventSource);
        Assert.NotNull(updater);
        Assert.False(eventSource.Listening);
        Assert.Equal(1, eventSource.DisposeCount);
        Assert.Equal(1, updater.DisposeCount);
        Assert.Equal(["stop-events", "dispose-async", "dispose-events", "globals"], events);
        Assert.Empty(cleanupErrors);

        await lifetime.DisposeAsync();
        await lifetime.DisposeAsync();
        Assert.Equal(1, failedConstructions);
        Assert.Equal(4, events.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailuresPreserveStartupExceptionAndAttemptEveryPhase(bool loggerThrows)
    {
        var original = new InvalidOperationException("startup");
        var stopFailure = new IOException("unhook");
        var disposalFailure = new IOException("container");
        var globalsFailure = new IOException("globals");
        List<Exception> errors = [];
        int stopCalls = 0, disposalCalls = 0, globalCalls = 0;
        var lifetime = new PluginLifetime();
        ServiceCollection registrations = [];
        registrations.AddSingleton(_ => new ThrowingService(() =>
        {
            disposalCalls++;
            throw disposalFailure;
        }));

        Exception observed = await Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.InitializeAsync(() =>
        {
            var services = registrations.BuildServiceProvider();
            lifetime.OwnServices(services);
            services.GetRequiredService<ThrowingService>();
            throw original;
        }, () =>
        {
            stopCalls++;
            throw stopFailure;
        }, () =>
        {
            globalCalls++;
            throw globalsFailure;
        }, error =>
        {
            errors.Add(error);
            if (loggerThrows)
                throw new IOException("logger");
        }, CancellationToken.None));

        Assert.Same(original, observed);
        Assert.Equal([stopFailure, disposalFailure, globalsFailure], errors);
        await lifetime.DisposeAsync();
        Assert.Equal(1, stopCalls);
        Assert.Equal(1, disposalCalls);
        Assert.Equal(1, globalCalls);
    }

    [Fact]
    public async Task SuccessfulStartupWaitsForAsyncDisposalBeforeReleasingGlobals()
    {
        List<string> events = [];
        var releaseDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime = new PluginLifetime();
        AsyncService? updater = null;
        ServiceCollection registrations = [];
        registrations.AddSingleton(_ => new AsyncService(events, async () =>
        {
            disposalStarted.SetResult();
            await releaseDisposal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }));
        await lifetime.InitializeAsync(() =>
        {
            var services = registrations.BuildServiceProvider();
            lifetime.OwnServices(services);
            updater = services.GetRequiredService<AsyncService>();
            return Task.CompletedTask;
        }, () =>
        {
            events.Add("stop-events");
            return ValueTask.CompletedTask;
        }, () => events.Add("globals"), _ => throw new Xunit.Sdk.XunitException("Unexpected cleanup failure"), CancellationToken.None);

        Task firstDispose = lifetime.DisposeAsync().AsTask();
        await disposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task secondDispose = lifetime.DisposeAsync().AsTask();
        Assert.False(firstDispose.IsCompleted);
        Assert.False(secondDispose.IsCompleted);
        Assert.DoesNotContain("globals", events);
        releaseDisposal.SetResult();
        await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["stop-events", "dispose-async", "globals"], events);
        Assert.Equal(1, updater!.DisposeCount);
    }

    [Fact]
    public async Task FailureBeforeContainerCreationStillReleasesGlobals()
    {
        var failure = new IOException("configuration read failed");
        int globalsReleased = 0;
        var lifetime = new PluginLifetime();
        Exception observed = await Assert.ThrowsAsync<IOException>(() => lifetime.InitializeAsync(
            () => throw failure, () => ValueTask.CompletedTask, () => globalsReleased++, _ => { }, CancellationToken.None));

        Assert.Same(failure, observed);
        await lifetime.DisposeAsync();
        Assert.Equal(1, globalsReleased);
    }

    [Fact]
    public async Task CancellationAfterInitializationStartsRollsBackOwnedServices()
    {
        using CancellationTokenSource cancellation = new();
        var initializationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> events = [];
        var lifetime = new PluginLifetime();
        ServiceCollection registrations = [];
        registrations.AddSingleton(_ => new AsyncService(events));
        Task load = lifetime.InitializeAsync(async () =>
        {
            var services = registrations.BuildServiceProvider();
            lifetime.OwnServices(services);
            services.GetRequiredService<AsyncService>();
            initializationStarted.SetResult();
            await finishInitialization.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }, () =>
        {
            events.Add("stop-events");
            return ValueTask.CompletedTask;
        }, () => events.Add("globals"), _ => { }, cancellation.Token);

        await initializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        finishInitialization.SetResult();
        OperationCanceledException observed = await Assert.ThrowsAsync<OperationCanceledException>(() => load.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, observed.CancellationToken);
        Assert.Equal(["stop-events", "dispose-async", "globals"], events);
        await lifetime.DisposeAsync();
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public async Task DisposalDuringInitializationWaitsForResourceOwnership()
    {
        var initializationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> events = [];
        var lifetime = new PluginLifetime();
        ServiceCollection registrations = [];
        registrations.AddSingleton(_ => new AsyncService(events));
        Task load = lifetime.InitializeAsync(async () =>
        {
            initializationStarted.SetResult();
            await finishInitialization.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var services = registrations.BuildServiceProvider();
            lifetime.OwnServices(services);
            services.GetRequiredService<AsyncService>();
        }, () => ValueTask.CompletedTask, () => events.Add("globals"), _ => { }, CancellationToken.None);

        await initializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task dispose = lifetime.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        finishInitialization.SetResult();
        await Task.WhenAll(load, dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["dispose-async", "globals"], events);
    }

    [Fact]
    public async Task DisposalBeforeInitializationPreventsResourceCreation()
    {
        var lifetime = new PluginLifetime();
        await lifetime.DisposeAsync();
        bool initialized = false;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lifetime.InitializeAsync(() =>
        {
            initialized = true;
            return Task.CompletedTask;
        }, () => ValueTask.CompletedTask, () => { }, _ => { }, CancellationToken.None));
        Assert.False(initialized);
    }

    private sealed class FailedInitializer { }

    private sealed class EventSource(List<string> events) : IDisposable
    {
        internal bool Listening { get; private set; } = true;
        internal int DisposeCount { get; private set; }

        internal void Stop()
        {
            Listening = false;
            events.Add("stop-events");
        }

        public void Dispose()
        {
            Assert.False(Listening);
            DisposeCount++;
            events.Add("dispose-events");
        }
    }

    private sealed class AsyncService(List<string> events, Func<Task>? wait = null) : IAsyncDisposable
    {
        internal int DisposeCount { get; private set; }

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (wait != null)
                await wait();
            events.Add("dispose-async");
        }
    }

    private sealed class ThrowingService(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
