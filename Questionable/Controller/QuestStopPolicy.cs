using Quest = Questionable.Domain.Quest;
using QuestProgress = Questionable.Controller.QuestController.QuestProgress;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class QuestStopPolicy
{
    internal const string UiStopReason = "UI stop";

    public static IReadOnlyList<string> ExcludedStopReasons { get; } = Array.AsReadOnly<string>([UiStopReason, "ESC pressed"]);

    private readonly Configuration _configuration;
    private readonly Action<string> _notifyOnFailure;
    private readonly Action<string> _notify;
    private readonly Action<string> _runCommand;
    private readonly IChatGui _chatGui;
    private readonly ILogger<QuestStopPolicy> _logger;

    public QuestStopPolicy(Configuration configuration, NotificationMasterIpc notifications,
        ICommandManager commands, IChatGui chatGui, ILogger<QuestStopPolicy> logger)
        : this(configuration, notifications.NotifyOnFailure, notifications.Notify,
            command => commands.ProcessCommand(command), chatGui, logger)
    {
    }

    internal QuestStopPolicy(Configuration configuration, Action<string> notifyOnFailure, Action<string> notify,
        Action<string> runCommand, IChatGui chatGui, ILogger<QuestStopPolicy> logger)
    {
        _configuration = configuration;
        _notifyOnFailure = notifyOnFailure;
        _notify = notify;
        _runCommand = runCommand;
        _chatGui = chatGui;
        _logger = logger;
    }

    public void Apply(string label, bool wasAutomatic)
    {
        if (!wasAutomatic || ExcludedStopReasons.Any(x => label.Equals(x, StringComparison.OrdinalIgnoreCase)))
            return;

        TryRun(() => _notifyOnFailure(_L("Automatic questing has stopped.")), "stop notification");
        if (_configuration.Stop is { RunCommandAfterStop: true } stop && stop.CommandAfterStop.StartsWith('/'))
            TryRun(() => _runCommand(stop.CommandAfterStop), "stop command");
    }

    public void ReportLevelReached(short currentLevel)
    {
        _logger.LogInformation("Reached level stop condition (level: {CurrentLevel}, target: {TargetLevel})", currentLevel, _configuration.Stop.TargetLevel);
        PrintAndNotify(_LF("Reached or exceeded target level {0}.", _configuration.Stop.TargetLevel));
    }

    public void ReportStopPoint(Quest quest)
    {
        _logger.LogInformation("Reached stopping point (quest: {QuestId})", quest.Id);
        var msg = _LF("Completed quest '{0}', which is configured as a stopping point.", quest.Info.Name);
        Print(msg);
        _notify(msg);
    }

    public void ReportStopAfterQuest(Quest quest)
    {
        _logger.LogInformation("Stopping after current quest as requested (quest: {QuestId})", quest.Id);
        PrintAndNotify(_LF("Completed quest '{0}', stopping as requested.", quest.Info.Name));
    }

    public void ReportStopBeforeTeleport(ITask nextTask)
    {
        string stopping = _L("Stopping before teleport as requested.");
        _logger.LogInformation("{Stopping} (upcoming task: {Task})", stopping, nextTask);
        PrintAndNotify(stopping);
    }

    public void ReportStopAfterAttune(EAetheryteLocation aetheryte)
    {
        string stopping = _L("Attuned to the aetheryte, stopping as requested.");
        _logger.LogInformation("{Stopping} (aetheryte: {Aetheryte})", stopping, aetheryte);
        PrintAndNotify(stopping);
    }

    public void ReportAcceptStop(Quest quest, bool configStop)
    {
        _logger.LogInformation("Reached accept stopping point (quest: {QuestId})", quest.Id);
        Print(configStop
            ? $"Accepted quest '{quest.Info.Name}', which is configured as a stopping point."
            : $"Accepted quest '{quest.Info.Name}', stopping as requested.");
    }

    public void ReportLevelTooLow(IQuestInfo quest, int playerLevel)
    {
        _logger.LogInformation(
            "Stopping automation, player level ({PlayerLevel}) < quest level ({QuestLevel}",
            playerLevel, quest.Level);
        PrintLevelStop(quest, playerLevel);
    }

    public void ReportMainScenarioLocked(IQuestInfo quest, int playerLevel)
    {
        _logger.LogInformation("Stopping automation, next MSQ {QuestId} needs level {QuestLevel} (player level {PlayerLevel})",
            quest.QuestId, quest.Level, playerLevel);
        PrintLevelStop(quest, playerLevel);
    }

    public void ReportNoSequence(QuestProgress progress) =>
        PrintError(_LF("Stopped: no path for '{0}' sequence {1}. Try Reload Data or report it with Stuck?",
            progress.Quest.Info.Name, progress.Sequence));

    public void ReportNoStep(QuestProgress progress) =>
        PrintError(_LF("Stopped: no path for '{0}' sequence {1} step {2}. Try Reload Data or report it with Stuck?",
            progress.Quest.Info.Name, progress.Sequence, progress.Step));

    public void ReportMissingCurrents(Quest quest, IReadOnlyList<uint> missing)
    {
        _logger.LogWarning("Aether currents not attuned in {Zone}: {Currents}", quest.Info.Name,
            string.Join(", ", quest.AllSteps()
                .Where(x => x.Step.AetherCurrentId is { } id && missing.Contains(id))
                .Select(x => $"{x.Step.AetherCurrentId} at {x.Step.Position}")));
        PrintError(_LF("Stopped: {0} aether current(s) in '{1}' are still not attuned. See /xllog for where they are.",
            missing.Count, quest.Info.Name));
    }

    public void ReportDeathLimit(int deaths) =>
        PrintError($"You died {deaths} times, manual intervention needed.");

    public void ReportTasksFailed(Exception e, QuestProgress? current)
    {
        _logger.LogError(e, "Failed to create tasks [{QuestId}, {Sequence}, {Step}]",
            current?.Quest.Id, current?.Sequence, current?.Step);
        PrintError("Failed to start next task sequence, please check /xllog for details.");
        _notifyOnFailure(_L("Failed to start next task sequence, stopping.") + " " + _L("Please try Reload Data, fixing manually, or reporting an issue with the 'Stuck?' button."));
    }

    private void PrintLevelStop(IQuestInfo quest, int playerLevel) =>
        Print(_LF("Stopped: '{0}' needs level {1} (you're level {2}).", quest.Name, quest.Level, playerLevel));

    private void PrintAndNotify(string message)
    {
        Print(message);
        if (_configuration.Notifications.NotifyOnStopCondition)
            _notify(message);
    }

    private void Print(string message) => _chatGui.Print(message, CommandHandler.MessageTag, CommandHandler.TagColor);

    private void PrintError(string message) => _chatGui.PrintError(message, CommandHandler.MessageTag, CommandHandler.TagColor);

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
