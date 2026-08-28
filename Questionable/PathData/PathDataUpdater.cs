using System.Security.Cryptography;
using System.Text.Json;

namespace Questionable.PathData;

/// <summary>
///     Downloads newer quest/gathering path bundles at runtime so path-only updates don't require a
///     full plugin release. The bundle is stored in <c>{ConfigDirectory}/PathData/</c> and picked up
///     by <see cref="QuestRegistry" />'s downloaded-bundle load tier.
/// </summary>
[RegisterSingleton]
internal sealed class PathDataUpdater : IDisposable
{
    private const string RepositoryUrl = "https://github.com/PunishXIV/Questionable";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Configuration _configuration;
    private readonly QuestRegistry _questRegistry;
    private readonly IFramework _framework;
    private readonly ILogger<PathDataUpdater> _logger;

    private readonly string _channel;
    private readonly string _pluginVersion;
    private int _running;
    internal bool WaitingForPluginUpdate;

    public PathDataUpdater(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        QuestRegistry questRegistry,
        IFramework framework,
        ILogger<PathDataUpdater> logger)
    {
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _questRegistry = questRegistry;
        _framework = framework;
        _logger = logger;

        _channel = pluginInterface.IsTesting ? "testing" : "latest";
        _pluginVersion = typeof(PathDataUpdater).Assembly.GetName().Version?.ToString() ?? "0";

        DiscardStaleBundle();
    }

    public DateTime StatusLastChanged { get; private set; } = DateTime.Now;
    private string _status = _L("Idle");
    /// <summary>Human-readable status for the config UI.</summary>
    public string Status
    {
        get
        {
            return _status;
        }
        private set
        {
            _status = value;
            StatusLastChanged = DateTime.Now;
        }
    }

    public void Dispose()
    {
    }

    /// <summary>Starts a background update check if auto-update is enabled.</summary>
    public void CheckForUpdates()
    {
        if (_configuration.PathData.AutoUpdate)
            StartUpdateCheck();
    }

    /// <summary>Starts a background update check regardless of the auto-update setting.</summary>
    public void CheckForUpdatesManually() => StartUpdateCheck();

    private void StartUpdateCheck()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await CheckForUpdatesAsync().ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        });
    }

    /// <summary>
    ///     Discards the cached bundle if it was downloaded by a different plugin version — the
    ///     freshly installed plugin's compiled paths become the new baseline. Runs synchronously in
    ///     the constructor, before the registry first loads.
    /// </summary>
    private void DiscardStaleBundle()
    {
        if (_configuration.PathData.BundlePluginVersion == _pluginVersion)
            return;

        string bundlePath = PathDataBundle.GetBundlePath(_pluginInterface);
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

    private async Task RetryAfterDelay(int retryCount = 1)
    {
        await Task.Delay(1000 * retryCount).ConfigureAwait(false);
        _configuration.PathData.InstalledDataVersion = 0;
        _configuration.PathData.BundlePluginVersion = null;
        Save();
        retryCount *= 2;
        await CheckForUpdatesAsync(retryCount).ConfigureAwait(false);
    }

    private async Task CheckForUpdatesAsync(int retryCount = 1)
    {
        Status = _L("Checking for path updates…");
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

        string manifestUrl = $"{RepositoryUrl}/releases/download/paths-{_channel}/manifest-{_channel}.json";
        _logger.LogDebug($"Requesting path updates from {manifestUrl}");
        PathDataManifest? manifest;
        try
        {
            string manifestJson = await http.GetStringAsync(new Uri(manifestUrl)).ConfigureAwait(false);
            manifest = JsonSerializer.Deserialize<PathDataManifest>(manifestJson);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to fetch the path data manifest; retrying");
            Status = _L("Update check failed") + $" ({retryCount})";
            await RetryAfterDelay(retryCount).ConfigureAwait(false);
            return;
        }

        if (manifest == null)
        {
            _logger.LogWarning("manifest was null; retrying");
            Status = _L("Update check failed") + $" ({retryCount})";
            await RetryAfterDelay(retryCount).ConfigureAwait(false);
            return;
        }

        _configuration.PathData.LastCheck = DateTimeOffset.UtcNow;

        // Gate A: a bundle that needs a newer plugin can't be used.
        if (!manifest.IsCompatibleWith(PathDataFormat.CurrentVersion))
        {
            _logger.LogInformation("Newer path data is available but requires a newer plugin version");
            Status = _L("Update the plugin for newer path data");
            WaitingForPluginUpdate = true;
            Save();
            return;
        }

        if (manifest.DataVersion <= _configuration.PathData.InstalledDataVersion)
        {
            if (_questRegistry.Count >= 500)
            {
                Status = _L("Path data is up to date");
                Save();
                return;
            }

            Status = _L("Path data from previous run missing, downloading path data...");
            Save();
        }

        if (string.IsNullOrEmpty(manifest.BundleUrl))
        {
            _logger.LogWarning("Path data manifest has no bundle URL; retrying");
            Status = _L("Update check failed") + $" ({retryCount})";
            await RetryAfterDelay(retryCount).ConfigureAwait(false);
            return;
        }

        Status = _L("Downloading path data…");
        byte[] zipBytes;
        try
        {
            zipBytes = await http.GetByteArrayAsync(new Uri(manifest.BundleUrl)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to download the path data bundle; retrying");
            Status = _L("Download failed") + $" ({retryCount})";
            await RetryAfterDelay(retryCount).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrEmpty(manifest.BundleSha256))
        {
            string actualSha = Convert.ToHexString(SHA256.HashData(zipBytes));
            if (!string.Equals(actualSha, manifest.BundleSha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Path data bundle checksum mismatch; retrying");
                Status = _L("Download failed (checksum mismatch)") + $" ({retryCount})";
                await RetryAfterDelay(retryCount).ConfigureAwait(false);
                return;
            }
        }

        // Atomic install: write to a temp file, then move into place.
        string bundlePath = PathDataBundle.GetBundlePath(_pluginInterface);
        string tempPath = bundlePath + ".tmp";
        try
        {
            Directory.CreateDirectory(PathDataBundle.GetDirectory(_pluginInterface));
            await File.WriteAllBytesAsync(tempPath, zipBytes).ConfigureAwait(false);
            File.Move(tempPath, bundlePath, overwrite: true);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to install the downloaded path data bundle; retrying");
            Status = _L("Install failed") + $" ({retryCount})";
            await RetryAfterDelay(retryCount).ConfigureAwait(false);
            return;
        }

        _configuration.PathData.InstalledDataVersion = manifest.DataVersion;
        _configuration.PathData.BundlePluginVersion = _pluginVersion;
        Save();

        _logger.LogInformation("Installed path data bundle (data version {DataVersion})", manifest.DataVersion);
        Status = _LF("Updated to data version {0}", manifest.DataVersion);

        await _framework.RunOnFrameworkThread(_questRegistry.Reload).ConfigureAwait(false);
    }

    private void Save() => _pluginInterface.SavePluginConfig(_configuration);
}
