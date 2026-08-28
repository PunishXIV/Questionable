using NotificationMasterAPI;

namespace Questionable.External;

[RegisterSingleton]
internal sealed class NotificationMasterIpc(IDalamudPluginInterface pluginInterface, Configuration configuration)
{
    private readonly NotificationMasterApi _api = new(pluginInterface);

    public bool Enabled => _api.IsIPCReady();

    public void Notify(string message)
    {
        var config = configuration.Notifications;
        if (!config.Enabled || !Enabled)
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
}