using FFXIVClientStructs.FFXIV.Client.Game;
using Character = Questionable.External.AutoRetainerIpc.Character;
using EAutomationType = Questionable.Controller.QuestController.EAutomationType;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class RetainerPass : IDisposable
{
    internal enum EState
    {
        Idle,
        Handing,
        Waiting,
        Returning,
        Switching,
        Starting,
    }

    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PassTimeout = TimeSpan.FromHours(2);
    // AutoRetainer may refuse to turn multi-mode on (e.g. in a duty); then the pass counts as done.
    private static readonly TimeSpan TurnOnTime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private readonly Configuration _configuration;
    private readonly AutoRetainerIpc _autoRetainer;
    private readonly LifestreamIpc _lifestream;
    private readonly NotificationMasterIpc _notifications;
    private readonly QuestController _questController;
    private readonly QuestStarter _questStarter;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly ICondition _condition;
    private readonly IFramework _framework;
    private readonly IChatGui _chatGui;
    private readonly ILogger<RetainerPass> _logger;

    private Character? _home;
    private bool _seenOn;
    private DateTime _nextPass = DateTime.MinValue;
    private DateTime _startedAt;
    private DateTime _nextTry;
    private DateTime _deadline;

    public RetainerPass(Configuration configuration, AutoRetainerIpc autoRetainer, LifestreamIpc lifestream,
        NotificationMasterIpc notifications, QuestController questController, QuestStarter questStarter,
        IClientState clientState, IObjectTable objectTable, ICondition condition, IFramework framework,
        IChatGui chatGui, ILogger<RetainerPass> logger)
    {
        _configuration = configuration;
        _autoRetainer = autoRetainer;
        _lifestream = lifestream;
        _notifications = notifications;
        _questController = questController;
        _questStarter = questStarter;
        _clientState = clientState;
        _objectTable = objectTable;
        _condition = condition;
        _framework = framework;
        _chatGui = chatGui;
        _logger = logger;

        _autoRetainer.KeepEnabled = () => IsActive;
        _framework.Update += Update;
    }

    public EState State { get; private set; }

    public bool IsActive => State != EState.Idle;

    public string? Status => IsActive && _home != null
        ? _LF("AutoRetainer multi-mode pass, then back to {0}", _home.Name)
        : null;

    internal static bool AnyReady(IEnumerable<Character> characters, Func<ulong, bool> retainersReady,
        Func<ulong, bool> vesselsReady) =>
        characters.Any(x => x.Enabled && retainersReady(x.ContentId) ||
                            x.WorkshopEnabled && vesselsReady(x.ContentId));

    public bool IsDue()
    {
        if (IsActive || DateTime.Now < _nextPass || !_configuration.General.SendRetainers ||
            _questController.AutomationType != EAutomationType.Automatic || !_autoRetainer.IsReady() ||
            _lifestream.GetVersion() == null || FreeBagSlots() < 2)
            return false;

        if (AnyReady(_autoRetainer.GetCharacters(), _autoRetainer.RetainersReady, _autoRetainer.VesselsReady))
            return true;

        // Checking every character is a handful of calls; not on every step.
        _nextPass = DateTime.Now + TimeSpan.FromMinutes(1);
        return false;
    }

    public bool Begin()
    {
        if (_autoRetainer.GetCharacters().FirstOrDefault(x => x.ContentId == CharacterRun.CurrentId) is not { } home)
        {
            _nextPass = DateTime.Now + Gap;
            return false;
        }

        _home = home;
        State = EState.Handing;
        _logger.LogInformation("Handing over to AutoRetainer for one multi-mode pass; coming back to {Name}@{World}",
            home.Name, home.World);
        return true;
    }

    public void Cancel()
    {
        if (!IsActive || _home == null)
            return;

        _logger.LogInformation("AutoRetainer pass cancelled in state {State}", State);
        string name = _home.Name;
        _nextPass = DateTime.Now + Gap;
        End();
        Print(_LF("Not going back to {0} after AutoRetainer's pass.", name));
    }

    private void Update(IFramework framework)
    {
        if (State == EState.Idle || _home is not { } home)
            return;

        DateTime now = DateTime.Now;
        if (State == EState.Handing)
        {
            _questController.StopForPass("AutoRetainer");
            _seenOn = false;
            _startedAt = now;
            _deadline = now + PassTimeout;
            State = EState.Waiting;
            if (_autoRetainer.StartPass())
                Print(_L("AutoRetainer is doing one multi-mode pass; questing carries on afterwards."));
            else
                Return(now);
            return;
        }

        if (State == EState.Waiting)
        {
            if (_questController.IsRunning)
            {
                Cancel();
                return;
            }

            if (now >= _deadline)
            {
                _autoRetainer.StopPass();
                Print(_L("AutoRetainer's pass took over 2 hours; stopped it."));
                Return(now);
                return;
            }

            if (_autoRetainer.IsMultiModeOn)
                _seenOn = true;
            else if (_seenOn || now >= _startedAt + TurnOnTime)
                Return(now);
            return;
        }

        if (now >= _deadline)
        {
            Fail(_LF("Couldn't get back to {0} after AutoRetainer's pass.", home.Name));
            return;
        }

        if (now < _nextTry)
            return;

        _nextTry = now + RetryTime;
        switch (State)
        {
            case EState.Returning:
                if (_lifestream.IsBusy || !IsPlayerReady())
                {
                    _deadline = now + Timeout;
                    return;
                }

                if (CharacterRun.CurrentId == home.ContentId)
                {
                    State = EState.Starting;
                    _nextTry = now + SettleTime;
                    _deadline = now + Timeout;
                }
                else if (_autoRetainer.CanRelog && _autoRetainer.Relog(home))
                {
                    State = EState.Switching;
                    _deadline = now + Timeout;
                }

                break;

            case EState.Switching:
                if (CharacterRun.CurrentId != home.ContentId || !IsPlayerReady())
                    return;

                State = EState.Starting;
                _nextTry = now + SettleTime;
                _deadline = now + Timeout;
                break;

            case EState.Starting:
                if (CharacterRun.CurrentId != home.ContentId || !IsPlayerReady() ||
                    !_questController.CanPrepareQuestTasks())
                    return;

                End();
                _questStarter.Start("AutoRetainer");
                break;
        }
    }

    private void Return(DateTime now)
    {
        // Something AutoRetainer left alone (e.g. subs while it only does retainers) would start a pass at every accept.
        bool leftOver = AnyReady(_autoRetainer.GetCharacters(), _autoRetainer.RetainersReady, _autoRetainer.VesselsReady);
        _nextPass = leftOver ? now + Gap : DateTime.MinValue;
        _logger.LogInformation("AutoRetainer's pass is done (left over: {LeftOver}); going back to {Name}", leftOver,
            _home?.Name);
        State = EState.Returning;
        _nextTry = now + SettleTime;
        _deadline = now + Timeout;
    }

    private bool IsPlayerReady() =>
        _clientState.IsLoggedIn && _objectTable[0] != null &&
        !_condition[ConditionFlag.BetweenAreas] && !_condition[ConditionFlag.BetweenAreas51];

    private static unsafe int FreeBagSlots()
    {
        InventoryManager* inventory = InventoryManager.Instance();
        return inventory != null ? (int)inventory->GetEmptySlotsInBag() : 0;
    }

    private void Fail(string message)
    {
        _logger.LogWarning("AutoRetainer pass failed in state {State}: {Message}", State, message);
        End();
        _chatGui.PrintError(message, CommandHandler.MessageTag, CommandHandler.TagColor);
        _notifications.NotifyOnFailure(message);
    }

    private void End()
    {
        State = EState.Idle;
        _home = null;
    }

    private void Print(string message) => _chatGui.Print(message, CommandHandler.MessageTag, CommandHandler.TagColor);

    public void Dispose()
    {
        _framework.Update -= Update;
        _autoRetainer.KeepEnabled = null;
    }
}
