using System.Security.Cryptography;
using System.Text.Json;

namespace Questionable.PathData;

[RegisterSingleton]
internal sealed class PathDataUpdater : IDisposable, IAsyncDisposable
{
    private static readonly string[] Domains =
    [
        "gh.qstxiv.dev",
        "github.com",
    ];
    private static readonly HashSet<string> AllowedHosts = new(Domains, StringComparer.OrdinalIgnoreCase);
    private const string RepositoryPath = "/PunishXIV/Questionable";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Configuration _configuration;
    private readonly Func<int> _questCount;
    private readonly Func<Action, Task> _runOnFrameworkThread;
    private readonly Action _reload;
    private readonly Func<HttpClient> _createHttpClient;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ILogger<PathDataUpdater> _logger;
    private readonly Lock _lifetimeLock = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _shutdownToken;
    private Task _updateTask = Task.CompletedTask;
    private Task? _disposeTask;
    private bool _disposed;

    private readonly string _channel;
    private readonly string _pluginVersion;
    internal bool WaitingForPluginUpdate;

    public PathDataUpdater(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        QuestRegistry questRegistry,
        IFramework framework,
        ILogger<PathDataUpdater> logger)
        : this(pluginInterface, configuration, () => questRegistry.Count,
            action => framework.RunOnFrameworkThread(action), questRegistry.Reload, logger)
    {
    }

    internal PathDataUpdater(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        Func<int> questCount,
        Func<Action, Task> runOnFrameworkThread,
        Action reload,
        ILogger<PathDataUpdater> logger,
        Func<HttpClient>? createHttpClient = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _questCount = questCount;
        _runOnFrameworkThread = runOnFrameworkThread;
        _reload = reload;
        _createHttpClient = createHttpClient ?? (() => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        _logger = logger;
        _shutdownToken = _shutdown.Token;

        _channel = pluginInterface.IsTesting ? "testing" : "latest";
        _pluginVersion = typeof(PathDataUpdater).Assembly.GetName().Version?.ToString() ?? "0";

        DiscardStaleBundle();
    }

    public DateTime StatusLastChanged { get; private set; } = DateTime.Now;
    private string _status = _L("Idle");
    public string Status
    {
        get
        {
            return _status;
        }
        private set
        {
            lock (_lifetimeLock)
            {
                if (_disposed)
                    return;
                _status = value;
                StatusLastChanged = DateTime.Now;
            }
        }
    }

    public void Dispose()
    {
        lock (_lifetimeLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _shutdown.Cancel();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifetimeLock)
        {
            Dispose();
            return new(_disposeTask ??= DrainAsync());
        }
    }

    private async Task DrainAsync()
    {
        try
        {
            await _updateTask.ConfigureAwait(false);
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    internal Task UpdateTask
    {
        get { lock (_lifetimeLock) return _updateTask; }
    }

    public void CheckForUpdates()
    {
        if (_configuration.PathData.AutoUpdate)
            StartUpdateCheck();
    }

    public void CheckForUpdatesManually() => StartUpdateCheck();

    private void StartUpdateCheck()
    {
        lock (_lifetimeLock)
        {
            if (_disposed || !_updateTask.IsCompleted)
                return;

            _updateTask = Task.Run(async () =>
            {
                try
                {
                    _shutdownToken.ThrowIfCancellationRequested();
                    await CheckForUpdatesAsync().ConfigureAwait(false);
                }
                catch (Exception) when (_shutdownToken.IsCancellationRequested)
                {
                    // Unload cancels downloads, retries and queued framework work.
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Path data update failed");
                    Status = _L("Update check failed; will retry next launch");
                }
            });
        }
    }

    private void DiscardStaleBundle()
    {
        if (_configuration.PathData.BundlePluginVersion == _pluginVersion)
            return;

        string bundlePath = PathDataBundle.GetFilePath(_pluginInterface);
        if (File.Exists(bundlePath))
        {
            try
            {
                File.Delete(bundlePath);
                _logger.LogInformation("Discarded path bundle from a previous plugin version");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not delete the stale path bundle");
            }
        }

        _configuration.PathData.InstalledDataVersion = 0;
        _configuration.PathData.BundlePluginVersion = null;
    }

    private async Task RetryAfterDelayAsync(int retryCount = 1)
    {
        _shutdownToken.ThrowIfCancellationRequested();
        if (retryCount >= 10)
        {
            Status = _L("Update check failed; will retry next launch");
            return;
        }
        int delayMilliseconds = Math.Min(1000 * (1 << retryCount), 60_000);
        await _delay(TimeSpan.FromMilliseconds(delayMilliseconds), _shutdownToken).ConfigureAwait(false);
        WhileActive(() =>
        {
            _configuration.PathData.InstalledDataVersion = 0;
            _configuration.PathData.BundlePluginVersion = null;
            Save();
        });
        retryCount++;
        await CheckForUpdatesAsync(retryCount).ConfigureAwait(false);
    }

    private async Task CheckForUpdatesAsync(int retryCount = 1)
    {
        _shutdownToken.ThrowIfCancellationRequested();
        Status = _L("Checking for path updates…");
        using HttpClient http = _createHttpClient();

        string host = Domains[retryCount % Domains.Length];
        string manifestUrl = $"https://{host}{RepositoryPath}/releases/download/paths-{_channel}/manifest-{_channel}.json";
        _logger.LogDebug($"Requesting path updates from {manifestUrl}");
        string manifestJson;
        PathDataManifest? manifest;
        try
        {
            manifestJson = await http.GetStringAsync(new Uri(manifestUrl), _shutdownToken).ConfigureAwait(false);
            manifest = JsonSerializer.Deserialize<PathDataManifest>(manifestJson);
        }
        catch (Exception e) when (!_shutdownToken.IsCancellationRequested)
        {
            _logger.LogWarning(e, "Failed to fetch the path data manifest; retrying");
            Status = _L("Update check failed") + $" ({retryCount})";
            await RetryAfterDelayAsync(retryCount).ConfigureAwait(false);
            return;
        }

        if (manifest == null)
        {
            _logger.LogWarning("manifest was null; retrying");
            Status = _L("Update check failed") + $" ({retryCount})";
            await RetryAfterDelayAsync(retryCount).ConfigureAwait(false);
            return;
        }

        WhileActive(() => _configuration.PathData.LastCheck = DateTimeOffset.UtcNow);

        // Gate A: a bundle that needs a newer plugin can't be used.
        if (!manifest.IsCompatibleWith(PathDataFormat.CurrentVersion))
        {
            _logger.LogInformation("Newer path data is available but requires a newer plugin version");
            Status = _L("Update the plugin for newer path data");
            WhileActive(() =>
            {
                WaitingForPluginUpdate = true;
                Save();
            });
            return;
        }

        if (manifest.DataVersion <= _configuration.PathData.InstalledDataVersion)
        {
            int questCount = 0;
            WhileActive(() => questCount = _questCount());
            if (questCount >= 500)
            {
                Status = _L("Path data is up to date");
                WhileActive(Save);
                return;
            }

            Status = _L("Path data from previous run missing, downloading path data...");
            WhileActive(Save);
        }

        if (!Uri.TryCreate(manifest.BundleUrl, UriKind.Absolute, out var bundleUri) ||
                bundleUri.Scheme != Uri.UriSchemeHttps ||
                !AllowedHosts.Contains(bundleUri.Host))
        {
            Status = _L("Update check failed: bundle download URL is invalid");
            _logger.LogWarning("Bundle URL had invalid domain: {Url}", manifest.BundleUrl);
            await RetryAfterDelayAsync(retryCount).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrEmpty(manifest.BundleUrl))
        {
            _logger.LogWarning("Path data manifest has no bundle URL; retrying");
            Status = _L("Update check failed") + $" ({retryCount})";
            await RetryAfterDelayAsync(retryCount).ConfigureAwait(false);
            return;
        }

        Status = _L("Downloading path data…");
        byte[] zipBytes;
        try
        {
            zipBytes = await http.GetByteArrayAsync(new Uri(manifest.BundleUrl), _shutdownToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!_shutdownToken.IsCancellationRequested)
        {
            _logger.LogWarning(e, "Failed to download the path data bundle; retrying");
            Status = _L("Download failed") + $" ({retryCount})";
            await RetryAfterDelayAsync(retryCount).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrEmpty(manifest.BundleSha256))
        {
            string actualSha = Convert.ToHexString(SHA256.HashData(zipBytes));
            if (!string.Equals(actualSha, manifest.BundleSha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Path data bundle checksum mismatch; retrying");
                Status = _L("Download failed (checksum mismatch)") + $" ({retryCount})";
                await RetryAfterDelayAsync(retryCount).ConfigureAwait(false);
                return;
            }
        }

        // Atomic install: write to a temp file, then move into place.
        string bundlePath = PathDataBundle.GetFilePath(_pluginInterface);
        string tempPath = bundlePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            WhileActive(() => Directory.CreateDirectory(PathDataBundle.GetDirectory(_pluginInterface)));
            await File.WriteAllBytesAsync(tempPath, zipBytes, _shutdownToken).ConfigureAwait(false);
            WhileActive(() =>
            {
                File.Move(tempPath, bundlePath, overwrite: true);
                _configuration.PathData.InstalledDataVersion = manifest.DataVersion;
                _configuration.PathData.BundlePluginVersion = _pluginVersion;
                Save();
            });
        }
        catch (Exception e) when (!_shutdownToken.IsCancellationRequested)
        {
            _logger.LogWarning(e, "Failed to install the downloaded path data bundle; retrying");
            Status = _L("Install failed") + $" ({retryCount})";
            await RetryAfterDelayAsync(retryCount).ConfigureAwait(false);
            return;
        }
        finally
        {
            try { File.Delete(tempPath); }
            catch (Exception e) { _logger.LogWarning(e, "Could not remove temporary path data bundle"); }
        }

        _logger.LogInformation("Installed path data bundle (data version {DataVersion})", manifest.DataVersion);
        Status = _LF("Updated to data version {0}", manifest.DataVersion);

        _shutdownToken.ThrowIfCancellationRequested();
        await _runOnFrameworkThread(() =>
        {
            lock (_lifetimeLock)
            {
                if (!_disposed)
                    _reload();
            }
        }).WaitAsync(_shutdownToken).ConfigureAwait(false);
    }

    private void WhileActive(Action action)
    {
        lock (_lifetimeLock)
        {
            _shutdownToken.ThrowIfCancellationRequested();
            action();
        }
    }

    private void Save() => _pluginInterface.SavePluginConfig(_configuration);
}
