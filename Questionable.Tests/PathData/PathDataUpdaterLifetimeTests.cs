using System.Net;
using System.Text.Json;
using Dalamud.Plugin;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Model;
using Questionable.PathData;
using Xunit;

#pragma warning disable CA2007

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
    public async Task OfflineRetries_KeepSavedVersion()
    {
        int requests = 0;
        await using var context = new Context((_, _) =>
        {
            Interlocked.Increment(ref requests);
            throw new HttpRequestException("offline");
        }, delay: (_, _) => Task.CompletedTask);
        context.Updater.CheckForUpdatesManually();
        await context.Updater.UpdateTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(10, requests);
        Assert.Equal(1, context.Config.PathData.InstalledDataVersion);
        Assert.NotNull(context.Config.PathData.BundlePluginVersion);
    }

    [Fact]
    public async Task QueuedReload_SkippedAfterDispose()
    {
        var queued = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameworkCompletion = Signal();
        int dispatches = 0;
        await using var context = new Context((request, _) => Task.FromResult(Response(request)), dispatch: action =>
        {
            // The last-check write and the install run at once; the third dispatch is the reload.
            if (Interlocked.Increment(ref dispatches) < 3)
            {
                action();
                return Task.CompletedTask;
            }

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
