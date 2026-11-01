#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
using static Questionable.External.IPCUtils;

namespace Questionable.External;

[RegisterSingleton<IAutoHookIpc, AutoHookIpc>]
internal sealed class AutoHookIpc : Ipc, IAutoHookIpc
{
    public override string InternalName => "AutoHook";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() => GetVersion() != null && _isPluginEnabled(), fallback: false);

    private readonly ILogger<AutoHookIpc> _logger;
    private readonly FishingOperationState _operation;
    private readonly Func<int> _ownedVersion;
    private readonly Func<bool> _fishingIdle;
    private readonly Func<Guid, string, (int, string)> _startOwned;
    private readonly Func<Guid, (int, bool, string)> _ownedStatus;
    private readonly Func<Guid, bool> _stopOwned;

    internal enum EOwnedStartResult { Uncertain = -1, Accepted = 0, Busy = 1, InvalidPreset = 2, NotReady = 3 }
    internal enum EOwnedFishingState { Unknown = 0, Starting = 1, Running = 2, Stopping = 3, Stopped = 4, Failed = 5, OwnershipLost = 6 }

    [EzIPC("GetPluginState")] private readonly Func<bool> _isPluginEnabled;
    [EzIPC("SetPluginState")] private readonly Action<bool> _setPluginEnabled;
    [EzIPC("GetAutoStartFishing")] private readonly Func<bool> _getAutoStartFishing;
    [EzIPC("SetAutoStartFishing")] private readonly Action<bool> _setAutoStartFishing;
    [EzIPC("SetAutoGigState")] private readonly Action<bool> _setAutoGigState;
    [EzIPC("SetPreset")] private readonly Action<string> _setPreset;
    [EzIPC("SetPresetAutogig")] private readonly Action<string> _setPresetAutogig;
    [EzIPC("CreateAndSelectAnonymousPreset")] private readonly Action<string> _createAndSelectAnonymousPreset;
    [EzIPC("ImportAndSelectPreset")] private readonly Action<string> _importAndSelectPreset;
    [EzIPC("DeleteSelectedPreset")] private readonly Action _deleteSelectedPreset;
    [EzIPC("DeleteAllAnonymousPresets")] private readonly Action _deleteAllAnonymousPresets;
    /// <returns>
    /// If bait was swapped successfully or already equipped
    /// </returns>
    [EzIPC("SwapBaitById")] private readonly Func<uint, bool> _swapBaitById;
    /// <returns>
    /// If bait was swapped successfully or already equipped
    /// </returns>
    [EzIPC("SwapBait")] private readonly Func<string, bool> _swapBait;
    /// <summary>
    /// Swaps the current swimbait slot by index (0,1,2).
    /// </summary>
    /// <returns>
    /// If bait was swapped successfully or already equipped
    /// </returns>
    [EzIPC("SwapSwimbaitByIndex")] private readonly Func<byte, bool> _swapSwimbaitByIndex;

    public AutoHookIpc(IDalamudPluginInterface pluginInterface, ILogger<AutoHookIpc> logger, FishingOperationState operation)
        : this(logger, operation,
            pluginInterface.GetIpcSubscriber<int>("AutoHook.GetOwnedFishingApiVersion").InvokeFunc,
            pluginInterface.GetIpcSubscriber<bool>("AutoHook.IsFishingIdle").InvokeFunc,
            pluginInterface.GetIpcSubscriber<Guid, string, (int, string)>("AutoHook.StartOwnedFishing").InvokeFunc,
            pluginInterface.GetIpcSubscriber<Guid, (int, bool, string)>("AutoHook.GetOwnedFishingStatus").InvokeFunc,
            pluginInterface.GetIpcSubscriber<Guid, bool>("AutoHook.StopOwnedFishing").InvokeFunc)
    {
        EzIPC.Init(this, "AutoHook", SafeWrapper.IPCException);
    }

    internal AutoHookIpc(ILogger<AutoHookIpc> logger, FishingOperationState operation, Func<int> version,
        Func<bool> idle, Func<Guid, string, (int, string)> start, Func<Guid, (int, bool, string)> status,
        Func<Guid, bool> stop)
    {
        _logger = logger;
        _operation = operation;
        _ownedVersion = version;
        _fishingIdle = idle;
        _startOwned = start;
        _ownedStatus = status;
        _stopOwned = stop;
    }

    public bool SupportsOwnedFishing()
    {
        try { return _ownedVersion() >= 1; }
        catch (Exception) { return false; }
    }

    public bool? GetFishingBusy()
    {
        try { return SupportsOwnedFishing() ? !_fishingIdle() : null; }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to observe AutoHook idle state");
            return null;
        }
    }

    public EOwnedStartResult StartOwnedFishing(string preset, out string message)
    {
        message = "";
        if (_operation.HasOperation)
            return EOwnedStartResult.Busy;
        if (!SupportsOwnedFishing())
            return EOwnedStartResult.NotReady;
        bool? busy = GetFishingBusy();
        if (busy != false)
            return busy == true ? EOwnedStartResult.Busy : EOwnedStartResult.NotReady;

        // The client supplies the ID, so even a lost start reply can be polled and stopped.
        Guid owner = _operation.BeginSubmission();
        try
        {
            (int result, message) = _startOwned(owner, preset);
            if (result == (int)EOwnedStartResult.Accepted)
            {
                _operation.AcceptSubmission(owner, owner);
                return EOwnedStartResult.Accepted;
            }
            if (result is >= (int)EOwnedStartResult.Busy and <= (int)EOwnedStartResult.NotReady)
            {
                _operation.RejectSubmission(owner);
                return (EOwnedStartResult)result;
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "AutoHook fishing submission has an uncertain result");
            message = e.Message;
        }

        _operation.MarkUncertain(owner);
        return EOwnedStartResult.Uncertain;
    }

    public void PollOperation()
    {
        if (_operation.OperationId is not { } owner)
            return;
        try
        {
            if (!SupportsOwnedFishing())
                _operation.ObserveStatus(owner, EOwnedFishingState.Unknown, false, "AutoHook is unavailable");
            else
            {
                var (state, started, message) = _ownedStatus(owner);
                _operation.ObserveStatus(owner, (EOwnedFishingState)state, started, message);
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Unable to poll AutoHook operation {OperationId}", owner);
            _operation.ObserveStatus(owner, EOwnedFishingState.Unknown, false, e.Message);
        }
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
        _operation.AbandonCurrent();
        RequestStop();
    }

    private void SendStop()
    {
        if (!_operation.TryBeginStop(out Guid owner))
            return;
        try { _operation.ObserveStop(owner, _stopOwned(owner)); }
        catch (Exception e) { _logger.LogDebug(e, "Unable to stop AutoHook operation {OperationId}", owner); }
    }

    public void ObserveIdle() => _operation.ObserveIdle(GetFishingBusy());

    public bool IsAvailable() =>
        IpcInvoke.SafeFunc(() =>
        {
            // Probe IPC only: IsPluginEnabled() would return false for an installed-but-disabled plugin,
            // which DoFish handles by enabling AutoHook. We only need to know whether IPC succeeded.
            _isPluginEnabled();
            return true;
        }, fallback: false, _logger, "AutoHook plugin is not available");

    public bool IsPluginEnabled() =>
        IpcInvoke.SafeFunc(() => _isPluginEnabled(), fallback: false, _logger, "Unable to get AutoHook plugin state");

    public bool SetPluginEnabled(bool enabled)
    {
        _logger.LogInformation("Setting AutoHook plugin state to {Enabled}", enabled);
        return IpcInvoke.SafeFunc(() =>
        {
            _setPluginEnabled(enabled);
            return true;
        }, fallback: false, _logger, "Unable to set AutoHook plugin state");
    }

    public bool GetAutoStartFishing() =>
        IpcInvoke.SafeFunc(() => _getAutoStartFishing(), fallback: false, _logger,
            "Unable to get AutoHook auto-start fishing state");

    public bool SetAutoStartFishing(bool enabled)
    {
        _logger.LogInformation("Setting AutoHook auto-start fishing to {Enabled}", enabled);
        return IpcInvoke.SafeFunc(() =>
        {
            _setAutoStartFishing(enabled);
            return true;
        }, fallback: false, _logger, "Unable to set AutoHook auto-start fishing state");
    }

    public bool SetAutoGigState(bool enabled)
    {
        _logger.LogInformation("Setting AutoHook auto-gig state to {Enabled}", enabled);
        return IpcInvoke.SafeFunc(() =>
        {
            _setAutoGigState(enabled);
            return true;
        }, fallback: false, _logger, "Unable to set AutoHook auto-gig state");
    }

    public bool SetPreset(string presetName)
    {
        _logger.LogInformation("Setting AutoHook preset to {Preset}", presetName);
        return IpcInvoke.SafeFunc(() =>
        {
            _setPreset(presetName);
            return true;
        }, fallback: false, _logger, "Unable to set AutoHook preset");
    }

    public bool SetPresetAutogig(string presetName)
    {
        _logger.LogInformation("Setting AutoHook autogig preset to {Preset}", presetName);
        return IpcInvoke.SafeFunc(() =>
        {
            _setPresetAutogig(presetName);
            return true;
        }, fallback: false, _logger, "Unable to set AutoHook autogig preset");
    }

    public bool CreateAndSelectAnonymousPreset(string compressedPresetJson)
    {
        _logger.LogInformation("Creating and selecting anonymous AutoHook preset");
        return IpcInvoke.SafeFunc(() =>
        {
            _createAndSelectAnonymousPreset(compressedPresetJson);
            return true;
        }, fallback: false, _logger, "Unable to create and select anonymous AutoHook preset");
    }

    public bool ImportAndSelectPreset(string compressedPresetJson)
    {
        _logger.LogInformation("Importing and selecting AutoHook preset");
        return IpcInvoke.SafeFunc(() =>
        {
            _importAndSelectPreset(compressedPresetJson);
            return true;
        }, fallback: false, _logger, "Unable to import and select AutoHook preset");
    }

    public bool DeleteSelectedPreset()
    {
        _logger.LogInformation("Deleting selected AutoHook preset");
        return IpcInvoke.SafeFunc(() =>
        {
            _deleteSelectedPreset();
            return true;
        }, fallback: false, _logger, "Unable to delete selected AutoHook preset");
    }

    public bool DeleteAllAnonymousPresets()
    {
        _logger.LogInformation("Deleting all anonymous AutoHook presets");
        return IpcInvoke.SafeFunc(() =>
        {
            _deleteAllAnonymousPresets();
            return true;
        }, fallback: false, _logger, "Unable to delete anonymous AutoHook presets");
    }

    public bool SwapBaitById(uint baitId)
    {
        _logger.LogInformation("Swapping AutoHook bait by id {BaitId}", baitId);
        return IpcInvoke.SafeFunc(() => _swapBaitById(baitId), fallback: false, _logger, "Unable to swap AutoHook bait by id");
    }

    public bool SwapBait(string baitNameOrId)
    {
        _logger.LogInformation("Swapping AutoHook bait {Bait}", baitNameOrId);
        return IpcInvoke.SafeFunc(() => _swapBait(baitNameOrId), fallback: false, _logger, "Unable to swap AutoHook bait");
    }

    public bool SwapSwimbaitByIndex(byte index)
    {
        _logger.LogInformation("Swapping AutoHook swimbait slot {Index}", index);
        return IpcInvoke.SafeFunc(() => _swapSwimbaitByIndex(index), fallback: false, _logger,
            "Unable to swap AutoHook swimbait");
    }
}
