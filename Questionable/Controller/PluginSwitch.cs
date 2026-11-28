namespace Questionable.Controller;

// Like WahTools does for AutoHook and GatherBuddy Reborn: switched off for the session while Questionable runs.
[RegisterSingleton]
internal sealed class PluginSwitch : IDisposable
{
    // Questionable stops and starts itself (AutoRetainer pass, character list); don't flip it back on in between.
    private static readonly TimeSpan GiveBackAfter = TimeSpan.FromSeconds(30);

    private readonly QuestController _questController;
    private readonly Configuration _configuration;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IFramework _framework;
    private readonly IChatGui _chatGui;
    private readonly ILogger<PluginSwitch> _logger;
    private readonly Target[] _targets;
    private DateTime _idleSince = DateTime.MinValue;

    public PluginSwitch(QuestController questController, Configuration configuration,
        IDalamudPluginInterface pluginInterface, ICommandManager commandManager, IFramework framework,
        IChatGui chatGui, ILogger<PluginSwitch> logger)
    {
        _questController = questController;
        _configuration = configuration;
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _framework = framework;
        _chatGui = chatGui;
        _logger = logger;
        _targets =
        [
            new("WigglyQuest", () => true,
                () => _L("WigglyQuest is switched off while Questionable runs; it comes back on when Questionable stops."),
                () => _configuration.WigglyQuestOffIn, x => _configuration.WigglyQuestOffIn = x),
            // Reborn answers Boss Mod's IPC names too, so Boss Mod's setting reads fail while both are loaded.
            new("BossModReborn", () => IsLoaded("BossMod"),
                () => _L("Boss Mod Reborn is switched off while Questionable runs; it comes back on when Questionable stops."),
                () => _configuration.BossModRebornOffIn, x => _configuration.BossModRebornOffIn = x),
        ];
        _framework.Update += Update;
    }

    private void Update(IFramework framework)
    {
        if (!EzThrottler.Throttle("PluginSwitch", 500))
            return;

        bool running = _questController.IsRunning ||
                       _questController.AutomationType != QuestController.EAutomationType.Manual;
        if (running)
        {
            _idleSince = DateTime.MinValue;
            foreach (Target target in _targets)
                if (IsLoaded(target.InternalName) && target.When())
                    SwitchOff(target);
            return;
        }

        if (!_targets.Any(SwitchedOff))
            return;

        if (_idleSince == DateTime.MinValue)
            _idleSince = DateTime.Now;
        if (DateTime.Now - _idleSince >= GiveBackAfter)
            SwitchBackOn();
    }

    // A temporary disable lasts until the game closes, so a reload of Questionable in the same game still owes the switch back.
    private static bool SwitchedOff(Target target) => target.OffIn() == Environment.ProcessId;

    private bool IsLoaded(string internalName) =>
        _pluginInterface.InstalledPlugins.Any(x => x.InternalName == internalName && x.IsLoaded);

    private void SwitchOff(Target target)
    {
        _commandManager.ProcessCommand($"/xldisableplugintemp \"{target.InternalName}\"");
        if (!SwitchedOff(target))
        {
            _logger.LogInformation("Switched {Plugin} off while Questionable runs", target.InternalName);
            _chatGui.Print(target.Message(), CommandHandler.MessageTag, CommandHandler.TagColor);
            Remember(target, Environment.ProcessId);
        }
    }

    private void SwitchBackOn()
    {
        foreach (Target target in _targets.Where(SwitchedOff))
        {
            if (!IsLoaded(target.InternalName) &&
                _pluginInterface.InstalledPlugins.Any(x => x.InternalName == target.InternalName))
            {
                _commandManager.ProcessCommand($"/xlenableplugintemp \"{target.InternalName}\"");
                _logger.LogInformation("Switched {Plugin} back on", target.InternalName);
            }

            Remember(target, 0);
        }
    }

    private void Remember(Target target, int processId)
    {
        target.SetOffIn(processId);
        _pluginInterface.SavePluginConfig(_configuration);
    }

    public void Dispose()
    {
        _framework.Update -= Update;
        if (_targets.Any(SwitchedOff))
            IpcInvoke.TryOnFrameworkThread(_framework, SwitchBackOn);
    }

    private sealed record Target(string InternalName, Func<bool> When, Func<string> Message, Func<int> OffIn,
        Action<int> SetOffIn);
}
