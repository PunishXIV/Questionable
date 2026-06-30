namespace Questionable.External;

internal interface IAutoHookIpc
{
  bool IsAvailable();

  bool IsPluginEnabled();

  bool SetPluginEnabled(bool enabled);

  bool GetAutoStartFishing();

  bool SetAutoStartFishing(bool enabled);

  bool SetAutoGigState(bool enabled);

  bool SetPreset(string presetName);

  bool SetPresetAutogig(string presetName);

  bool CreateAndSelectAnonymousPreset(string compressedPresetJson);

  bool ImportAndSelectPreset(string compressedPresetJson);

  bool DeleteSelectedPreset();

  bool DeleteAllAnonymousPresets();

  bool SwapBaitById(uint baitId);

  bool SwapBait(string baitNameOrId);

  bool SwapSwimbaitByIndex(byte index);
}
