namespace Questionable;

internal sealed class PluginLifetime : IAsyncDisposable
{
#pragma warning disable CA2213 // Retained for repeated/concurrent disposal; no wait handle is created.
    private readonly SemaphoreSlim _gate = new(1, 1);
#pragma warning restore CA2213
    private IAsyncDisposable? _services;
    private Func<ValueTask>? _stopEventSources;
    private Action? _releaseGlobals;
    private Action<Exception>? _logCleanupError;
    private bool _initializationStarted;
    private bool _disposed;

    internal async Task InitializeAsync(Func<Task> initialize, Func<ValueTask> stopEventSources,
        Action releaseGlobals, Action<Exception> logCleanupError, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initializationStarted)
                throw new InvalidOperationException("Plugin initialization has already started.");

            _initializationStarted = true;
            _stopEventSources = stopEventSources;
            _releaseGlobals = releaseGlobals;
            _logCleanupError = logCleanupError;
            try
            {
                await initialize().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                await DisposeCoreAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal void SetServices(IAsyncDisposable services)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_services != null)
            throw new InvalidOperationException("Plugin services are already set.");
        _services = services;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask DisposeCoreAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (_stopEventSources != null)
                await _stopEventSources().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            ReportCleanupError(e);
        }

        try
        {
            if (Interlocked.Exchange(ref _services, null) is { } services)
                await services.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            ReportCleanupError(e);
        }
        finally
        {
            try
            {
                _releaseGlobals?.Invoke();
            }
            catch (Exception e)
            {
                ReportCleanupError(e);
            }
        }
    }

    private void ReportCleanupError(Exception exception)
    {
        try
        {
            _logCleanupError?.Invoke(exception);
        }
        catch
        {
            // A logging failure must not replace the startup exception or skip remaining cleanup.
        }
    }
}
