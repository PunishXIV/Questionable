namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class QuestStopPolicy
{
    internal const string UiStopReason = "UI stop";

    public static IReadOnlyList<string> CommandExceptions { get; } = Array.AsReadOnly<string>([UiStopReason, "ESC pressed"]);

    private readonly Configuration _configuration;
    private readonly Action<string> _notify;
    private readonly Action<string> _runCommand;
    private readonly ILogger<QuestStopPolicy> _logger;

    public QuestStopPolicy(Configuration configuration, NotificationMasterIpc notifications,
        ICommandManager commands, ILogger<QuestStopPolicy> logger)
        : this(configuration, notifications.NotifyOnFailure, command => commands.ProcessCommand(command), logger)
    {
    }

    internal QuestStopPolicy(Configuration configuration, Action<string> notify,
        Action<string> runCommand, ILogger<QuestStopPolicy> logger)
    {
        _configuration = configuration;
        _notify = notify;
        _runCommand = runCommand;
        _logger = logger;
    }

    public void Apply(string label, bool wasAutomatic)
    {
        if (!wasAutomatic || CommandExceptions.Any(x => label.Equals(x, StringComparison.OrdinalIgnoreCase)))
            return;

        TryRun(() => _notify(_L("Automatic questing has stopped.")), "stop notification");
        if (_configuration.Stop is { RunCommandAfterStop: true } stop && stop.CommandAfterStop.StartsWith('/'))
            TryRun(() => _runCommand(stop.CommandAfterStop), "stop command");
    }

    private void TryRun(Action action, string operation)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to stop {Operation}", operation);
        }
    }
}
