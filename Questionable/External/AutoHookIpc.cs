using System;
using ECommons.EzIpcManager;
using Microsoft.Extensions.Logging;
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
namespace Questionable.External;

internal sealed class AutoHookIpc
{
  private readonly ILogger<AutoHookIpc> _logger;

  [EzIPC("GetPluginState")] private readonly Func<bool> _isPluginEnabled;
  [EzIPC("SetPluginState")] private readonly Action<bool> _setPluginEnabled;
  [EzIPC] private readonly Func<bool> _getAutoStartFishing;
  [EzIPC] private readonly Action<bool> _setAutoStartFishing;
  [EzIPC] private readonly Action<bool> _setAutoGigState;
  [EzIPC] private readonly Action<string> _setPreset;
  [EzIPC] private readonly Action<string> _setPresetAutogig;
  [EzIPC] private readonly Action<string> _createAndSelectAnonymousPreset;
  [EzIPC] private readonly Action<string> _importAndSelectPreset;
  [EzIPC] private readonly Action _deleteSelectedPreset;
  [EzIPC] private readonly Action _deleteAllAnonymousPresets;
  /// <returns>
  /// If bait was swapped successfully or already equipped
  /// </returns>
  [EzIPC] private readonly Func<uint, bool> _swapBaitById;
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
  [EzIPC] private readonly Func<byte, bool> _swapSwimbaitByIndex;

  public AutoHookIpc(ILogger<AutoHookIpc> logger)
  {
    _logger = logger;
    EzIPC.Init(this, "AutoHook", SafeWrapper.IPCException);
  }

  public bool IsPluginEnabled() =>
      IpcInvoke.SafeFunc(() => _isPluginEnabled(), false, _logger, "Unable to get AutoHook plugin state");

  public bool SetPluginEnabled(bool enabled)
  {
    _logger.LogInformation("Setting AutoHook plugin state to {Enabled}", enabled);
    return IpcInvoke.SafeFunc(() =>
    {
      _setPluginEnabled(enabled);
      return true;
    }, false, _logger, "Unable to set AutoHook plugin state");
  }

  public bool GetAutoStartFishing() =>
      IpcInvoke.SafeFunc(() => _getAutoStartFishing(), false, _logger,
          "Unable to get AutoHook auto-start fishing state");

  public bool SetAutoStartFishing(bool enabled)
  {
    _logger.LogInformation("Setting AutoHook auto-start fishing to {Enabled}", enabled);
    return IpcInvoke.SafeFunc(() =>
    {
      _setAutoStartFishing(enabled);
      return true;
    }, false, _logger, "Unable to set AutoHook auto-start fishing state");
  }

  public bool SetAutoGigState(bool enabled)
  {
    _logger.LogInformation("Setting AutoHook auto-gig state to {Enabled}", enabled);
    return IpcInvoke.SafeFunc(() =>
    {
      _setAutoGigState(enabled);
      return true;
    }, false, _logger, "Unable to set AutoHook auto-gig state");
  }

  public bool SetPreset(string presetName)
  {
    _logger.LogInformation("Setting AutoHook preset to {Preset}", presetName);
    return IpcInvoke.SafeFunc(() =>
    {
      _setPreset(presetName);
      return true;
    }, false, _logger, "Unable to set AutoHook preset");
  }

  public bool SetPresetAutogig(string presetName)
  {
    _logger.LogInformation("Setting AutoHook autogig preset to {Preset}", presetName);
    return IpcInvoke.SafeFunc(() =>
    {
      _setPresetAutogig(presetName);
      return true;
    }, false, _logger, "Unable to set AutoHook autogig preset");
  }

  public bool CreateAndSelectAnonymousPreset(string compressedPresetJson)
  {
    _logger.LogInformation("Creating and selecting anonymous AutoHook preset: {CompressedPresetJson}", compressedPresetJson);
    return IpcInvoke.SafeFunc(() =>
    {
      _createAndSelectAnonymousPreset(compressedPresetJson);
      return true;
    }, false, _logger, "Unable to create and select anonymous AutoHook preset");
  }

  public bool ImportAndSelectPreset(string compressedPresetJson)
  {
    _logger.LogInformation("Importing and selecting AutoHook preset: {CompressedPresetJson}", compressedPresetJson);
    return IpcInvoke.SafeFunc(() =>
    {
      _importAndSelectPreset(compressedPresetJson);
      return true;
    }, false, _logger, "Unable to import and select AutoHook preset");
  }

  public bool DeleteSelectedPreset()
  {
    _logger.LogInformation("Deleting selected AutoHook preset");
    return IpcInvoke.SafeFunc(() =>
    {
      _deleteSelectedPreset();
      return true;
    }, false, _logger, "Unable to delete selected AutoHook preset");
  }

  public bool DeleteAllAnonymousPresets()
  {
    _logger.LogInformation("Deleting all anonymous AutoHook presets");
    return IpcInvoke.SafeFunc(() =>
    {
      _deleteAllAnonymousPresets();
      return true;
    }, false, _logger, "Unable to delete anonymous AutoHook presets");
  }

  public bool SwapBaitById(uint baitId)
  {
    _logger.LogInformation("Swapping AutoHook bait by id {BaitId}", baitId);
    return IpcInvoke.SafeFunc(() => _swapBaitById(baitId), false, _logger, "Unable to swap AutoHook bait by id");
  }

  public bool SwapBait(string baitNameOrId)
  {
    _logger.LogInformation("Swapping AutoHook bait {Bait}", baitNameOrId);
    return IpcInvoke.SafeFunc(() => _swapBait(baitNameOrId), false, _logger, "Unable to swap AutoHook bait");
  }

  public bool SwapSwimbaitByIndex(byte index)
  {
    _logger.LogInformation("Swapping AutoHook swimbait slot {Index}", index);
    return IpcInvoke.SafeFunc(() => _swapSwimbaitByIndex(index), false, _logger,
        "Unable to swap AutoHook swimbait");
  }
}
