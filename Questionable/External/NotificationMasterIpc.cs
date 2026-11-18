using System.Runtime.InteropServices;
using NotificationMasterAPI;
using static Questionable.External.IPCUtils;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace Questionable.External;

[RegisterSingleton]
internal sealed partial class NotificationMasterIpc(IDalamudPluginInterface pluginInterface, Configuration configuration) : Ipc
{
    private const uint SoundAsync = 0x0001, SoundNoDefault = 0x0002, SoundFileName = 0x00020000;

    public override string InternalName => "NotificationMaster";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() => GetVersion() != null && Enabled, fallback: false);
    private readonly NotificationMasterApi _api = new(pluginInterface);

    public bool Enabled => _api.IsIPCReady();

    public static string SoundFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    public void Notify(string message)
    {
        var config = configuration.Notifications;
        if (!config.Enabled || config.OnlyWhenUnfocused && IsGameFocused())
            return;

        PlaySound(config.Sound);
        if (!Enabled)
            return;

        if (config.ShowTrayMessage)
            _api.DisplayTrayNotification("Questionable", message);

        if (config.FlashTaskbar)
            _api.FlashTaskbarIcon();
    }

    public void NotifyOnFailure(string message)
    {
        if (configuration.Notifications.NotifyOnCriticalFailure)
            Notify(message);
    }

    public static void PlaySound(string sound)
    {
        if (!string.IsNullOrEmpty(sound))
            PlaySoundW(Path.Combine(SoundFolder, sound), 0, SoundFileName | SoundAsync | SoundNoDefault);
    }

    private static unsafe bool IsGameFocused() => GameFramework.Instance() is var framework && framework != null && !framework->WindowInactive;

    [LibraryImport("winmm.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySoundW(string sound, nint module, uint flags);
}
