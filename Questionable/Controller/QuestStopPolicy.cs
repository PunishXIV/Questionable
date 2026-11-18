using EDestination = Questionable.Configuration.EDestination;
using EEndAction = Questionable.Configuration.EEndAction;
using Quest = Questionable.Domain.Quest;
using QuestProgress = Questionable.Controller.QuestController.QuestProgress;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class QuestStopPolicy
{
    internal const int MaxWaitSeconds = 3600;

    // Lifestream needs a moment before it reports itself busy.
    private static readonly TimeSpan TravelStart = TimeSpan.FromSeconds(3);

    internal const string UiStopReason = "UI stop";

    public static IReadOnlyList<string> ExcludedStopReasons { get; } = Array.AsReadOnly<string>([UiStopReason, "ESC pressed"]);

    private readonly Configuration _configuration;
    private readonly Action<string> _notifyOnFailure;
    private readonly Action<string> _notify;
    private readonly Action<string> _runCommand;
    private readonly IChatGui _chatGui;
    private readonly ILogger<QuestStopPolicy> _logger;
    private readonly Action? _saveConfig;
    private readonly Func<bool> _isTraveling;
    private Ending? _ending;

    public QuestStopPolicy(Configuration configuration, NotificationMasterIpc notifications,
        ICommandManager commands, ChatFunctions chatFunctions, IDalamudPluginInterface pluginInterface,
        LifestreamIpc lifestream, IChatGui chatGui, ILogger<QuestStopPolicy> logger)
        : this(configuration, notifications.NotifyOnFailure, notifications.Notify,
            command => Run(command, commands.ProcessCommand, chatFunctions.ExecuteCommand), chatGui, logger,
            () => pluginInterface.SavePluginConfig(configuration), () => lifestream.IsBusy)
    {
    }

    internal QuestStopPolicy(Configuration configuration, Action<string> notifyOnFailure, Action<string> notify,
        Action<string> runCommand, IChatGui chatGui, ILogger<QuestStopPolicy> logger, Action? saveConfig = null,
        Func<bool>? isTraveling = null)
    {
        _isTraveling = isTraveling ?? (() => false);
        _configuration = configuration;
        _notifyOnFailure = notifyOnFailure;
        _notify = notify;
        _runCommand = runCommand;
        _chatGui = chatGui;
        _logger = logger;
        _saveConfig = saveConfig;
    }

    // Saved right away; otherwise the quest comes back after a reload unless another setting is saved first.
    internal void RemoveStopQuest(ElementId questId)
    {
        _configuration.Stop.QuestsToStopAfter.Remove(questId);
        _configuration.Stop.SetPreset(questId, null);
        try
        {
            _saveConfig?.Invoke();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to save the stop list");
        }
    }

    // Set by the character list run. It gets whether the end actions apply to this stop, and answers
    // true when another character is up next, so the final action waits for the last one.
    internal Func<bool, bool>? StopHandler { get; set; }

    internal bool IsListRun { get; set; }

    // A finished run reached its goal or a stop the user asked for; anything else is a problem stop.
    public void Apply(string label, bool wasAutomatic, bool finished = false, EEndAction? endAction = null)
    {
        if (!wasAutomatic)
            return;

        if (ExcludedStopReasons.Any(x => label.Equals(x, StringComparison.OrdinalIgnoreCase)))
        {
            StopHandler?.Invoke(false);
            return;
        }

        TryRun(() => _notifyOnFailure(_L("Automatic questing has stopped.")), "stop notification");

        Configuration.StopConfiguration stop = _configuration.Stop;
        if (!finished && !stop.RunAfterProblemStop)
        {
            StopHandler?.Invoke(false);
            return;
        }

        bool nextCharacter = StopHandler?.Invoke(true) == true;
        EEndAction action = endAction ?? stop.EndAction;
        string? final = nextCharacter ? null : GetCommand(action);
        string? travel = GetCommand(stop.Destination);
        DateTime now = DateTime.Now;
        if (travel != null)
            TryRun(() => _runCommand(travel), "travel");

        _ending = new Ending
        {
            Commands = stop.RunCommandAfterStop ? [.. GetCommands(stop.CommandAfterStop)] : [],
            Final = final,
            Next = nextCharacter ? _L("the next character") : final != null ? GetName(action) : null,
            ReadyAt = travel != null ? now + TravelStart : now,
            // With nothing after the commands there is nothing to wait for.
            Wait = final != null || nextCharacter
                ? TimeSpan.FromSeconds(Math.Clamp(stop.WaitSeconds, 0, MaxWaitSeconds))
                : TimeSpan.Zero,
        };
        Update(now);
    }

    internal bool IsEnding => _ending != null;

    public string? EndingStatus
    {
        get
        {
            if (_ending is not { } ending)
                return null;
            if (!ending.CommandsSent)
                return _L("Run ended: travelling first.");
            if (ending.Next == null)
                return _L("Run ended: waiting for travel to finish.");

            int seconds = (int)Math.Ceiling((ending.ReadyAt - DateTime.Now).TotalSeconds);
            return seconds > 0
                ? _LF("Run ended: {0} in {1} s.", ending.Next, seconds)
                : _LF("Run ended: {0} after travel.", ending.Next);
        }
    }

    public void CancelEnding()
    {
        if (_ending == null)
            return;

        _logger.LogInformation("Run end cancelled");
        _ending = null;
    }

    // Travel first, then the commands, then the wait and the final action, each only once nothing is travelling.
    // CharacterRun calls this every frame.
    internal void Update(DateTime now)
    {
        while (_ending is { } ending && now >= ending.ReadyAt && !_isTraveling())
        {
            if (!ending.CommandsSent)
            {
                ending.CommandsSent = true;
                foreach (string command in ending.Commands)
                    TryRun(() => _runCommand(command), "stop command");
                ending.ReadyAt = now + ending.Wait;
                continue;
            }

            _ending = null;
            if (ending.Final is { } final)
                TryRun(() => _runCommand(final), "end action");
        }
    }

    private sealed class Ending
    {
        public required List<string> Commands { get; init; }
        public string? Final { get; init; }
        public string? Next { get; init; }
        public required DateTime ReadyAt { get; set; }
        public TimeSpan Wait { get; init; }
        public bool CommandsSent { get; set; }
    }

    internal static IEnumerable<string> GetCommands(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.StartsWith('/'));

    internal static string? GetCommand(EEndAction action) => action switch
    {
        EEndAction.LogOut => "/logout",
        EEndAction.CloseGame => "/shutdown",
        _ => null,
    };

    internal static string GetName(EEndAction action) => action switch
    {
        EEndAction.LogOut => _L("Log out"),
        EEndAction.CloseGame => _L("Close the game"),
        _ => _L("Nothing"),
    };

    internal static string? GetCommand(EDestination destination) => destination switch
    {
        EDestination.HomeWorld => "/li",
        EDestination.Auto => "/li auto",
        EDestination.Inn => "/li inn",
        EDestination.PrivateEstate => "/li home",
        EDestination.FreeCompany => "/li fc",
        EDestination.Apartment => "/li apt",
        EDestination.GrandCompany => "/li gc",
        _ => null,
    };

    internal static string GetName(EDestination destination) => destination switch
    {
        EDestination.HomeWorld => _L("Home world"),
        EDestination.Auto => _L("Home (Lifestream's /li auto)"),
        EDestination.Inn => _L("Inn room"),
        EDestination.PrivateEstate => _L("Private estate"),
        EDestination.FreeCompany => _L("Free Company estate"),
        EDestination.Apartment => _L("Apartment"),
        EDestination.GrandCompany => _L("Grand Company"),
        _ => _L("Stay here"),
    };

    // Dalamud only knows plugin commands; everything else goes to the game's chat box.
    internal static void Run(string command, Func<string, bool> runPlugin, Action<string> runGame)
    {
        if (!runPlugin(command))
            runGame(command);
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

    public void ReportPresetLoaded(Quest quest, string preset, bool stopping)
    {
        _logger.LogInformation("Stop point queued preset {Preset} (quest: {QuestId}, stopping: {Stopping})",
            preset, quest.Id, stopping);
        PrintAndNotify(stopping
            ? _LF("Completed quest '{0}', preset '{1}' is queued for the next start.", quest.Info.Name, preset)
            : _LF("Completed quest '{0}', continuing with preset '{1}'.", quest.Info.Name, preset));
    }

    public void ReportPresetMissing(Quest quest) =>
        PrintError(_LF("The preset chosen for '{0}' no longer exists, stopping instead.", quest.Info.Name));

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

    public void ReportOnlySetAsideLeft(int count) =>
        Print(_LF("Stopped: the {0} quest(s) left wait on a duty you run yourself. Run it, then press Start.", count));

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

    public void ReportStuckRetry(QuestProgress current, int seconds, int attempt, int maxTries)
    {
        _logger.LogInformation("No progress for {Seconds} s, retrying step ({Attempt}/{Max}) [{QuestId}, {Sequence}, {Step}]",
            seconds, attempt, maxTries, current.Quest.Id, current.Sequence, current.Step);
        Print(_LF("No progress for {0} s, retrying the step ({1}/{2}).", seconds, attempt, maxTries));
    }

    public void ReportStuckLimit(Quest quest, int retries)
    {
        _logger.LogWarning("Still stuck after {Retries} retries (quest: {QuestId})", retries, quest.Id);
        PrintError(_LF("Stopped: still stuck on '{0}' after {1} retries. Report it with Stuck? if it keeps happening.",
            quest.Info.Name, retries));
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
