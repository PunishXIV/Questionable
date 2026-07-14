namespace Questionable.External;

internal interface IAutoHookIpc
{
    /// <summary>
    /// Whether the AutoHook plugin is installed and IPC is reachable.
    /// Unlike <see cref="IsPluginEnabled"/>, this returns true when the plugin is installed but disabled.
    /// </summary>
    bool IsAvailable();

    /// <summary>
    /// Gets the AutoHook plugin state.
    /// </summary>
    /// <returns>The AutoHook plugin state.</returns>
    bool IsPluginEnabled();

    /// <summary>
    /// Sets the AutoHook plugin state.
    /// </summary>
    /// <param name="enabled">Whether to enable the AutoHook plugin.</param>
    /// <returns>If command was called, not if the plugin state was set successfully</returns>
    bool SetPluginEnabled(bool enabled);

    /// <summary>
    /// Gets the AutoHook auto-start fishing state.
    /// </summary>
    /// <returns>The AutoHook auto-start fishing state.</returns>
    bool GetAutoStartFishing();

    /// <summary>
    /// Sets the AutoHook auto-start fishing state.
    /// </summary>
    /// <param name="enabled">Whether to enable auto-start fishing.</param>
    /// <returns>If command was called, not if the auto-start fishing state was set successfully</returns>
    bool SetAutoStartFishing(bool enabled);

    /// <summary>
    /// Sets the AutoHook auto-gig state.
    /// </summary>
    /// <param name="enabled">Whether to enable auto-gig.</param>
    /// <returns>If command was called, not if the auto-gig state was set successfully</returns>
    bool SetAutoGigState(bool enabled);

    /// <summary>
    /// Sets the AutoHook custom preset.
    /// </summary>
    /// <param name="presetName">The name of the preset to set.</param>
    /// <returns>If command was called, not if the preset was set successfully</returns>
    bool SetPreset(string presetName);

    /// <summary>
    /// Sets the AutoHook autogig preset.
    /// </summary>
    /// <param name="presetName">The name of the preset to set.</param>
    /// <returns>If command was called, not if the preset was set successfully</returns>
    bool SetPresetAutogig(string presetName);

    /// <summary>
    /// Creates and selects an anonymous AutoHook preset. This prefixes the preset name with "anon_".
    /// </summary>
    /// <param name="compressedPresetJson">The GZip-compressed and base64-encoded JSON string of the preset to create and select.</param>
    /// <returns>If command was called, not if the preset was created and selected successfully</returns>
    bool CreateAndSelectAnonymousPreset(string compressedPresetJson);

    /// <summary>
    /// Imports and selects a custom AutoHook preset.
    /// </summary>
    /// <param name="compressedPresetJson">The GZip-compressed and base64-encoded JSON string of the preset to import and select.</param>
    /// <returns>If command was called, not if the preset was imported and selected successfully</returns>
    bool ImportAndSelectPreset(string compressedPresetJson);

    /// <summary>
    /// Deletes the currently selected AutoHook preset.
    /// </summary>
    /// <returns>If command was called, not if the preset was deleted successfully</returns>
    bool DeleteSelectedPreset();

    /// <summary>
    /// Deletes all AutoHook custom presets beginning with "anon_".
    /// </summary>
    /// <returns>If command was called, not if the presets were deleted successfully</returns>
    bool DeleteAllAnonymousPresets();

    /// <summary>
    /// Swaps the current bait slot by id.
    /// </summary>
    /// <param name="baitId">The id of the bait to swap to.</param>
    /// <returns>If bait was swapped successfully or already equipped</returns>
    bool SwapBaitById(uint baitId);

    /// <summary>
    /// Swaps the current bait slot by name or id.
    /// </summary>
    /// <param name="baitNameOrId">The name or id of the bait to swap to.</param>
    /// <returns>If bait was swapped successfully or already equipped</returns>
    bool SwapBait(string baitNameOrId);

    /// <summary>
    /// Swaps the current swimbait slot by index (0,1,2).
    /// </summary>
    /// <param name="index">The index of the swimbait slot to swap to.</param>
    /// <returns>If swimbait was swapped successfully or already equipped</returns>
    bool SwapSwimbaitByIndex(byte index);
}
