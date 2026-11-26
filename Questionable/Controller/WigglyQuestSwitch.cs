namespace Questionable.Controller;

// Like WahTools does for AutoHook and GatherBuddy Reborn: switched off for the session while Questionable runs.
[RegisterSingleton]
internal sealed class WigglyQuestSwitch : IDisposable
{
    private const string InternalName = "WigglyQuest";
    // Questionable stops and starts itself (AutoRetainer pass, character list); don't flip it back on in between.
    private static readonly TimeSpan GiveBackAfter = TimeSpan.FromSeconds(30);

    private readonly QuestController _questController;
    private readonly Configuration _configuration;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IFramework _framework;
    private readonly IChatGui _chatGui;
    private readonly ILogger<WigglyQuestSwitch> _logger;
    private DateTime _idleSince = DateTime.MinValue;

    public WigglyQuestSwitch(QuestController questController, Configuration configuration,
        IDalamudPluginInterface pluginInterface, ICommandManager commandManager, IFramework framework,
        IChatGui chatGui, ILogger<WigglyQuestSwitch> logger)
    {
        _questController = questController;
        _configuration = configuration;
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _framework = framework;
        _chatGui = chatGui;
        _logger = logger;
        _framework.Update += Update;
    }

    // A temporary disable lasts until the game closes, so a reload of Questionable in the same game still owes the switch back.
    private bool SwitchedOff => _configuration.WigglyQuestOffIn == Environment.ProcessId;

    private void Update(IFramework framework)
    {
        if (!EzThrottler.Throttle("WigglyQuestSwitch", 500))
            return;

        bool running = _questController.IsRunning ||
                       _questController.AutomationType != QuestController.EAutomationType.Manual;
        if (running)
        {
            _idleSince = DateTime.MinValue;
            if (IsLoaded())
                SwitchOff();
            return;
        }

        if (!SwitchedOff)
            return;

        if (_idleSince == DateTime.MinValue)
            _idleSince = DateTime.Now;
        if (DateTime.Now - _idleSince >= GiveBackAfter)
            SwitchBackOn();
    }

    private bool IsLoaded() =>
        _pluginInterface.InstalledPlugins.Any(x => x.InternalName == InternalName && x.IsLoaded);

    private void SwitchOff()
    {
        _commandManager.ProcessCommand($"/xldisableplugintemp \"{InternalName}\"");
        if (!SwitchedOff)
        {
            _logger.LogInformation("Switched WigglyQuest off while Questionable runs");
            _chatGui.Print(_L("WigglyQuest is switched off while Questionable runs; it comes back on when Questionable stops."),
                CommandHandler.MessageTag, CommandHandler.TagColor);
            Remember(Environment.ProcessId);
        }
    }

    private void SwitchBackOn()
    {
        if (!IsLoaded() && _pluginInterface.InstalledPlugins.Any(x => x.InternalName == InternalName))
        {
            _commandManager.ProcessCommand($"/xlenableplugintemp \"{InternalName}\"");
            _logger.LogInformation("Switched WigglyQuest back on");
        }

        Remember(0);
    }

    private void Remember(int processId)
    {
        _configuration.WigglyQuestOffIn = processId;
        _pluginInterface.SavePluginConfig(_configuration);
    }

    public void Dispose()
    {
        _framework.Update -= Update;
        if (SwitchedOff)
            IpcInvoke.TryOnFrameworkThread(_framework, SwitchBackOn);
    }
}
