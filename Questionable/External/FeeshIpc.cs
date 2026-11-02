using Dalamud.Plugin.Ipc;
using static Questionable.External.IPCUtils;

namespace Questionable.External;

/// <summary>WahTools' Feesh catch jobs: one job per owner, no job IDs.</summary>
[RegisterSingleton<IFeeshIpc, FeeshIpc>]
internal sealed class FeeshIpc : Ipc, IFeeshIpc, IDisposable
{
    internal const string Owner = "Questionable";

    private readonly ILogger<FeeshIpc> _logger;
    private readonly FishingOperationState _operation;
    private readonly Func<bool> _isBusy;
    private readonly Func<uint, string> _taggedStand;
    private readonly Func<string, uint, int, bool, bool> _catch;
    private readonly Func<string> _catching;
    private readonly Func<string, bool> _cancelCatch;
    private readonly Func<string, uint, bool> _releaseCatch;
    private readonly ICallGateSubscriber<string, bool, object?>? _catchDone;
    private readonly object _catchDoneLock = new();
    private bool? _catchDoneResult;
    private uint? _heldFish;

    public FeeshIpc(IDalamudPluginInterface pluginInterface, ILogger<FeeshIpc> logger, FishingOperationState operation)
        : this(logger, operation,
            pluginInterface.GetIpcSubscriber<bool>("WahTools.IsBusy").InvokeFunc,
            pluginInterface.GetIpcSubscriber<uint, string>("WahTools.Feesh.TaggedStand").InvokeFunc,
            pluginInterface.GetIpcSubscriber<string, uint, int, bool, bool>("WahTools.Feesh.Catch").InvokeFunc,
            pluginInterface.GetIpcSubscriber<string>("WahTools.Feesh.Catching").InvokeFunc,
            pluginInterface.GetIpcSubscriber<string, bool>("WahTools.Feesh.CancelCatch").InvokeFunc,
            pluginInterface.GetIpcSubscriber<string, uint, bool>("WahTools.Feesh.ReleaseCatch").InvokeFunc)
    {
        _catchDone = pluginInterface.GetIpcSubscriber<string, bool, object?>("WahTools.Feesh.CatchDone");
        _catchDone.Subscribe(OnCatchDone);
    }

    internal FeeshIpc(ILogger<FeeshIpc> logger, FishingOperationState operation, Func<bool> isBusy,
        Func<uint, string> taggedStand, Func<string, uint, int, bool, bool> startCatch, Func<string> catching,
        Func<string, bool> cancelCatch, Func<string, uint, bool> releaseCatch)
    {
        _logger = logger;
        _operation = operation;
        _isBusy = isBusy;
        _taggedStand = taggedStand;
        _catch = startCatch;
        _catching = catching;
        _cancelCatch = cancelCatch;
        _releaseCatch = releaseCatch;
    }

    public override string InternalName => "WahTools";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IsAvailable();
    public bool IsAvailable() => ReadCatching() != null;
    public bool? GetBusy() => Read(_isBusy);

    public bool CanCatch(uint fishId)
    {
        try
        {
            return !string.IsNullOrEmpty(_taggedStand(fishId));
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to ask Feesh where to catch {FishId}", fishId);
            return false;
        }
    }

    public FeeshStartResult StartCatch(uint fishId, int additionalCount, out string message)
    {
        message = "";
        if (_operation.HasOperation)
            return FeeshStartResult.Busy;
        if (fishId == 0 || additionalCount <= 0)
            return FeeshStartResult.Invalid;
        if (!IsAvailable())
            return FeeshStartResult.NotReady;
        if (GetBusy() != false)
            return FeeshStartResult.Busy;

        Guid owner = _operation.BeginSubmission();
        lock (_catchDoneLock)
            _catchDoneResult = null;
        try
        {
            _logger.LogInformation("Requesting {Count} additional {FishId} from Feesh", additionalCount, fishId);
            if (!_catch(Owner, fishId, additionalCount, true))
            {
                _operation.RejectSubmission(owner);
                return GetBusy() == true ? FeeshStartResult.Busy : FeeshStartResult.Refused;
            }

            _operation.AcceptCatch(owner);
            _heldFish = fishId;
            return FeeshStartResult.Started;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Feesh catch submission has an uncertain result");
            _operation.MarkUncertain(owner);
            _heldFish = fishId;
            message = e.Message;
            return FeeshStartResult.Uncertain;
        }
    }

    public void PollOperation()
    {
        if (_operation.OperationId is not { } owner)
            return;

        string? text = ReadCatching();
        bool? catchDone;
        lock (_catchDoneLock)
            catchDone = _catchDoneResult;
        _operation.ObserveCatch(owner, ParseState(text), text ?? "Feesh is unavailable", catchDone, GetBusy());
        SendStop();
        if (_operation.RequiresReconciliation)
            ObserveIdle();
    }

    public void RequestStop()
    {
        _operation.RequestStop();
        SendStop();
    }

    public void CancelOwned()
    {
        if (!_operation.HasOperation)
            return;
        _operation.AbandonCurrent();
        RequestStop();
        if (_heldFish is { } fishId)
            ReleaseCatch(fishId);
    }

    public void ObserveIdle() => _operation.ObserveIdle(GetBusy());

    public void ReleaseCatch(uint fishId)
    {
        try
        {
            _releaseCatch(Owner, fishId);
            if (_heldFish == fishId)
                _heldFish = null;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to release Feesh's hold on {FishId}", fishId);
        }
    }

    /// <summary>
    /// Reads the first word of <c>Catching</c>: "fishing", then "finishing" while the rod and travel settle, then
    /// the result ("done|partial|failed|cancelled|stopped ..."), which stays until the next job.
    /// </summary>
    internal static EFeeshJobState ParseState(string? text)
    {
        if (text == null)
            return EFeeshJobState.Unavailable;

        string trimmed = text.TrimStart();
        int end = 0;
        while (end < trimmed.Length && char.IsLetter(trimmed[end]))
            end++;
        return trimmed[..end].ToUpperInvariant() switch
        {
            "" when trimmed.Length == 0 => EFeeshJobState.None,
            "FISHING" => EFeeshJobState.Fishing,
            "FINISHING" => EFeeshJobState.Finishing,
            "DONE" => EFeeshJobState.Done,
            "PARTIAL" => EFeeshJobState.Partial,
            "FAILED" => EFeeshJobState.Failed,
            "CANCELLED" or "CANCELED" => EFeeshJobState.Cancelled,
            "STOPPED" => EFeeshJobState.Stopped,
            _ => EFeeshJobState.Unrecognized,
        };
    }

    internal void OnCatchDone(string owner, bool done)
    {
        if (owner != Owner)
            return;
        // May arrive off the framework thread; the next poll applies it.
        lock (_catchDoneLock)
            _catchDoneResult = done;
    }

    private void SendStop()
    {
        if (!_operation.TryBeginStop(out Guid owner))
            return;
        try
        {
            _operation.ObserveStop(owner, _cancelCatch(Owner));
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to cancel Feesh catch job {OperationId}", owner);
        }
    }

    private string? ReadCatching()
    {
        try
        {
            return _catching() ?? "";
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool? Read(Func<bool> call)
    {
        try
        {
            return call();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose() => _catchDone?.Unsubscribe(OnCatchDone);
}
