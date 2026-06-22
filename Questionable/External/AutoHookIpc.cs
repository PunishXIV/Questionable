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

  public AutoHookIpc(ILogger<AutoHookIpc> logger)
  {
    _logger = logger;
    EzIPC.Init(this, "AutoHook", SafeWrapper.IPCException);
  }

  /// <summary>
  /// Gets the AutoHook plugin state.
  /// </summary>
  /// <returns>The AutoHook plugin state.</returns>
  public bool IsPluginEnabled() =>
      IpcInvoke.SafeFunc(() => _isPluginEnabled(), false, _logger, "Unable to get AutoHook plugin state");

  /// <summary>
  /// Sets the AutoHook plugin state.
  /// </summary>
  /// <param name="enabled">Whether to enable the AutoHook plugin.</param>
  /// <returns>If command was called, not if the plugin state was set successfully</returns>
  public bool SetPluginEnabled(bool enabled)
  {
    _logger.LogInformation("Setting AutoHook plugin state to {Enabled}", enabled);
    return IpcInvoke.SafeFunc(() =>
    {
      _setPluginEnabled(enabled);
      return true;
    }, false, _logger, "Unable to set AutoHook plugin state");
  }

  /// <summary>
  /// Gets the AutoHook auto-start fishing state.
  /// </summary>
  /// <returns>The AutoHook auto-start fishing state.</returns>
  public bool GetAutoStartFishing() =>
      IpcInvoke.SafeFunc(() => _getAutoStartFishing(), false, _logger,
          "Unable to get AutoHook auto-start fishing state");

  /// <summary>
  /// Sets the AutoHook auto-start fishing state.
  /// </summary>
  /// <param name="enabled">Whether to enable auto-start fishing.</param>
  /// <returns>If command was called, not if the auto-start fishing state was set successfully</returns>
  public bool SetAutoStartFishing(bool enabled)
  {
    _logger.LogInformation("Setting AutoHook auto-start fishing to {Enabled}", enabled);
    return IpcInvoke.SafeFunc(() =>
    {
      _setAutoStartFishing(enabled);
      return true;
    }, false, _logger, "Unable to set AutoHook auto-start fishing state");
  }

  /// <summary>
  /// Sets the AutoHook auto-gig state.
  /// </summary>
  /// <param name="enabled">Whether to enable auto-gig.</param>
  /// <returns>If command was called, not if the auto-gig state was set successfully</returns>
  public bool SetAutoGigState(bool enabled)
  {
    _logger.LogInformation("Setting AutoHook auto-gig state to {Enabled}", enabled);
    return IpcInvoke.SafeFunc(() =>
    {
      _setAutoGigState(enabled);
      return true;
    }, false, _logger, "Unable to set AutoHook auto-gig state");
  }

  /// <summary>
  /// Sets the AutoHook custom preset.
  /// </summary>
  /// <param name="presetName">The name of the preset to set.</param>
  /// <returns>If command was called, not if the preset was set successfully</returns>
  public bool SetPreset(string presetName)
  {
    _logger.LogInformation("Setting AutoHook preset to {Preset}", presetName);
    return IpcInvoke.SafeFunc(() =>
    {
      _setPreset(presetName);
      return true;
    }, false, _logger, "Unable to set AutoHook preset");
  }

  /// <summary>
  /// Sets the AutoHook autogig preset.
  /// </summary>
  /// <param name="presetName">The name of the preset to set.</param>
  /// <returns>If command was called, not if the preset was set successfully</returns>
  public bool SetPresetAutogig(string presetName)
  {
    _logger.LogInformation("Setting AutoHook autogig preset to {Preset}", presetName);
    return IpcInvoke.SafeFunc(() =>
    {
      _setPresetAutogig(presetName);
      return true;
    }, false, _logger, "Unable to set AutoHook autogig preset");
  }

  /// <summary>
  /// Creates and selects an anonymous AutoHook preset. This prefixes the preset name with "anon_".
  /// </summary>
  /// <param name="compressedPresetJson">The GZip-compressed and base64-encoded JSON string of the preset to create and select.</param>
  /// <returns>If command was called, not if the preset was created and selected successfully</returns>
  public bool CreateAndSelectAnonymousPreset(string compressedPresetJson)
  {
    _logger.LogInformation("Creating and selecting anonymous AutoHook preset");
    return IpcInvoke.SafeFunc(() =>
    {
      _createAndSelectAnonymousPreset(compressedPresetJson);
      return true;
    }, false, _logger, "Unable to create and select anonymous AutoHook preset");
  }

  /// <summary>
  /// Imports and selects a custom AutoHook preset.
  /// </summary>
  /// <param name="compressedPresetJson">The GZip-compressed and base64-encoded JSON string of the preset to import and select.</param>
  /// <returns>If command was called, not if the preset was imported and selected successfully</returns>
  public bool ImportAndSelectPreset(string compressedPresetJson)
  {
    _logger.LogInformation("Importing and selecting AutoHook preset");
    return IpcInvoke.SafeFunc(() =>
    {
      _importAndSelectPreset(compressedPresetJson);
      return true;
    }, false, _logger, "Unable to import and select AutoHook preset");
  }

  /// <summary>
  /// Deletes the currently selected AutoHook preset.
  /// </summary>
  /// <returns>If command was called, not if the preset was deleted successfully</returns>
  public bool DeleteSelectedPreset()
  {
    _logger.LogInformation("Deleting selected AutoHook preset");
    return IpcInvoke.SafeFunc(() =>
    {
      _deleteSelectedPreset();
      return true;
    }, false, _logger, "Unable to delete selected AutoHook preset");
  }

  /// <summary>
  /// Deletes all AutoHook custom presets beginning with "anon_".
  /// </summary>
  /// <returns>If command was called, not if the presets were deleted successfully</returns>
  public bool DeleteAllAnonymousPresets()
  {
    _logger.LogInformation("Deleting all anonymous AutoHook presets");
    return IpcInvoke.SafeFunc(() =>
    {
      _deleteAllAnonymousPresets();
      return true;
    }, false, _logger, "Unable to delete anonymous AutoHook presets");
  }

  /// <summary>
  /// Swaps the current bait slot by id.
  /// </summary>
  /// <param name="baitId">The id of the bait to swap to.</param>
  /// <returns>If bait was swapped successfully or already equipped</returns>
  public bool SwapBaitById(uint baitId)
  {
    _logger.LogInformation("Swapping AutoHook bait by id {BaitId}", baitId);
    return IpcInvoke.SafeFunc(() => _swapBaitById(baitId), false, _logger, "Unable to swap AutoHook bait by id");
  }

  /// <summary>
  /// Swaps the current bait slot by name or id.
  /// </summary>
  /// <param name="baitNameOrId">The name or id of the bait to swap to.</param>
  /// <returns>If bait was swapped successfully or already equipped</returns>
  public bool SwapBait(string baitNameOrId)
  {
    _logger.LogInformation("Swapping AutoHook bait {Bait}", baitNameOrId);
    return IpcInvoke.SafeFunc(() => _swapBait(baitNameOrId), false, _logger, "Unable to swap AutoHook bait");
  }

  /// <summary>
  /// Swaps the current swimbait slot by index (0,1,2).
  /// </summary>
  /// <param name="index">The index of the swimbait slot to swap to.</param>
  /// <returns>If swimbait was swapped successfully or already equipped</returns>
  public bool SwapSwimbaitByIndex(byte index)
  {
    _logger.LogInformation("Swapping AutoHook swimbait slot {Index}", index);
    return IpcInvoke.SafeFunc(() => _swapSwimbaitByIndex(index), false, _logger,
        "Unable to swap AutoHook swimbait");
  }
}
