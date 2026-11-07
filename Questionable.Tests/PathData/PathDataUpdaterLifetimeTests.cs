using System.Net;
using System.Text.Json;
using Dalamud.Plugin;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Model;
using Questionable.PathData;
using Xunit;

#pragma warning disable CA2007 // Test awaits retain xUnit's synchronization context.

namespace Questionable.Tests.PathData;

public sealed class PathDataUpdaterLifetimeTests
{
    [Fact]
    public async Task SuccessfulUpdateInstallsSavesAndReloads()
    {
        await using var context = new Context((request, _) => Task.FromResult(Response(request)));
        context.Updater.CheckForUpdatesManually();
        await context.Updater.UpdateTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Bundle, await File.ReadAllBytesAsync(context.BundlePath));
        Assert.Equal(2, context.Config.PathData.InstalledDataVersion);
        Assert.Equal(1, context.Saves);
        Assert.Equal(1, context.Reloads);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(context.BundlePath)!, "*.tmp"));
    }

    [Fact]
    public async Task DisposeInManifest_DrainsWithoutSaving()
    {
        var started = Signal();
        bool cancelled = false;
        await using var context = new Context(async (_, token) =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cancelled = token.IsCancellationRequested; }
            return new(HttpStatusCode.OK);
        });
        context.Updater.CheckForUpdatesManually();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await context.Updater.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cancelled);
        Assert.True(context.Updater.UpdateTask.IsCompletedSuccessfully);
        Assert.Equal(0, context.Saves);
        Assert.Equal(0, context.Reloads);
        Assert.False(File.Exists(context.BundlePath));
    }

    [Fact]
    public async Task DisposeInDownload_KeepsInstalledBundle()
    {
        var downloading = Signal();
        await using var context = new Context(async (request, token) =>
        {
            if (IsManifest(request)) return Response(request);
            downloading.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Response(request);
        });
        Directory.CreateDirectory(Path.GetDirectoryName(context.BundlePath)!);
        byte[] previous = [8, 9, 10];
        await File.WriteAllBytesAsync(context.BundlePath, previous);
        context.Updater.CheckForUpdatesManually();
        await downloading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await context.Updater.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(previous, await File.ReadAllBytesAsync(context.BundlePath));
        Assert.Equal(1, context.Config.PathData.InstalledDataVersion);
        Assert.Equal(0, context.Saves);
        Assert.Equal(0, context.Reloads);
    }

    [Fact]
    public async Task DisposeInRetry_KeepsSavedVersion()
    {
        var waiting = Signal();
        int requests = 0;
        bool cancelled = false;
        await using var context = new Context((_, _) =>
        {
            Interlocked.Increment(ref requests);
            throw new HttpRequestException("offline");
        }, delay: async (_, token) =>
        {
            waiting.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cancelled = token.IsCancellationRequested; }
        });
        context.Updater.CheckForUpdatesManually();
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await context.Updater.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cancelled);
        Assert.Equal(1, requests);
        Assert.Equal(1, context.Config.PathData.InstalledDataVersion);
        Assert.NotNull(context.Config.PathData.BundlePluginVersion);
        Assert.Equal(0, context.Saves);
    }

    [Fact]
    public async Task QueuedReload_SkippedAfterDispose()
    {
        var queued = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameworkCompletion = Signal();
        await using var context = new Context((request, _) => Task.FromResult(Response(request)), dispatch: action =>
        {
            queued.TrySetResult(action);
            return frameworkCompletion.Task;
        });
        context.Updater.CheckForUpdatesManually();
        Action reload = await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, context.Saves); // Install committed before shutdown started.
        await context.Updater.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        reload();
        frameworkCompletion.TrySetResult();

        Assert.Equal(0, context.Reloads);
        Assert.Equal(1, context.Saves);
        Assert.Equal(Bundle, await File.ReadAllBytesAsync(context.BundlePath));
    }

    [Fact]
    public async Task LateDownload_NotInstalledAfterDispose()
    {
        var downloading = Signal();
        var release = Signal();
        await using var context = new Context(async (request, _) =>
        {
            if (IsManifest(request)) return Response(request);
            downloading.TrySetResult();
            await release.Task;
            return Response(request); // Deliberately ignores transport cancellation.
        });
        context.Updater.CheckForUpdatesManually();
        await downloading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task disposal = context.Updater.DisposeAsync().AsTask();
        release.TrySetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(File.Exists(context.BundlePath));
        Assert.Equal(0, context.Saves);
        Assert.Equal(0, context.Reloads);
    }

    [Fact]
    public async Task EarlyOrRepeatedDispose_SendsNoRequests()
    {
        int requests = 0;
        await using var context = new Context((request, _) =>
        {
            requests++;
            return Task.FromResult(Response(request));
        });
#pragma warning disable CA1849 // Exercise the synchronous cancellation contract before asynchronous draining.
        context.Updater.Dispose();
#pragma warning restore CA1849
        await context.Updater.DisposeAsync();
        await context.Updater.DisposeAsync();
        context.Updater.CheckForUpdatesManually();
        context.Updater.CheckForUpdates();
        await context.Updater.UpdateTask;

        Assert.Equal(0, requests);
        Assert.Equal(0, context.Saves);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneUpdate()
    {
        var started = Signal();
        var release = Signal();
        int requests = 0;
        await using var context = new Context(async (request, token) =>
        {
            Interlocked.Increment(ref requests);
            if (IsManifest(request))
            {
                started.TrySetResult();
                await release.Task.WaitAsync(token);
            }
            return Response(request);
        });
        context.Updater.CheckForUpdatesManually();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        context.Updater.CheckForUpdatesManually();
        context.Updater.CheckForUpdates();
        release.TrySetResult();
        await context.Updater.UpdateTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, requests); // One manifest and one bundle.
        Assert.Equal(1, context.Reloads);
    }

    [Fact]
    public async Task UpToDate_SavesWithoutDownloading()
    {
        int requests = 0;
        await using var context = new Context((_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Manifest(1)) });
        });
        context.Updater.CheckForUpdatesManually();
        await context.Updater.UpdateTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, requests);
        Assert.Equal(1, context.Saves);
        Assert.Equal(0, context.Reloads);
    }

    private static readonly byte[] Bundle = [1, 2, 3, 4];
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static bool IsManifest(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.EndsWith(".json", StringComparison.Ordinal);
    private static string Manifest(long version = 2) => JsonSerializer.Serialize(new PathDataManifest
    {
        DataVersion = version,
        MinPluginDataFormat = PathDataFormat.CurrentVersion,
        BundleUrl = "https://github.com/PunishXIV/Questionable/releases/download/paths-testing/bundle.zip",
    });
    private static HttpResponseMessage Response(HttpRequestMessage request) => new(HttpStatusCode.OK)
    {
        Content = IsManifest(request) ? new StringContent(Manifest()) : new ByteArrayContent(Bundle),
    };

    private sealed class Context : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "Questionable-updater-test-" + Guid.NewGuid().ToString("N"));
        public Configuration Config { get; } = new();
        public PathDataUpdater Updater { get; }
        public string BundlePath => Path.Combine(_directory, "PathData", "bundle.zip");
        public int Saves;
        public int Reloads;

        public Context(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
            Func<Action, Task>? dispatch = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            var plugin = Substitute.For<IDalamudPluginInterface>();
            plugin.ConfigDirectory.Returns(new DirectoryInfo(_directory));
            plugin.When(x => x.SavePluginConfig(Config)).Do(_ => Interlocked.Increment(ref Saves));
            Config.PathData.BundlePluginVersion = typeof(PathDataUpdater).Assembly.GetName().Version?.ToString();
            Config.PathData.InstalledDataVersion = 1;
            Config.PathData.AutoUpdate = true;
            Updater = new(plugin, Config, () => 600,
                dispatch ?? (action => { action(); return Task.CompletedTask; }),
                () => Interlocked.Increment(ref Reloads), NullLogger<PathDataUpdater>.Instance,
                () => new HttpClient(new Handler(respond)), delay);
        }

        public async ValueTask DisposeAsync()
        {
            await Updater.DisposeAsync();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
