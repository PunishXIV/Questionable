using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA2007

namespace Questionable.Tests;

public sealed class PluginLifetimeTests
{
    [Fact]
    public async Task LateStartupFailure_UnhooksAndDisposes()
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
            lifetime.SetServices(services);
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

    [Fact]
    public async Task CancelledStartup_RollsBackServices()
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
            lifetime.SetServices(services);
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
