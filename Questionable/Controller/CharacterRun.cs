using Character = Questionable.External.AutoRetainerIpc.Character;
using EAutomationType = Questionable.Controller.QuestController.EAutomationType;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class CharacterRun : IDisposable
{
    internal enum EState
    {
        Idle,
        Questing,
        Leaving,
        Switching,
        Starting,
    }

    // /li auto and a fresh login need a moment before the next step.
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private readonly Configuration _configuration;
    private readonly AutoRetainerIpc _autoRetainer;
    private readonly LifestreamIpc _lifestream;
    private readonly NotificationMasterIpc _notifications;
    private readonly QuestController _questController;
    private readonly QuestStarter _questStarter;
    private readonly ConfigShare _configShare;
    private readonly ICommandManager _commandManager;
    private readonly QuestStopPolicy _stopPolicy;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly ICondition _condition;
    private readonly IFramework _framework;
    private readonly IChatGui _chatGui;
    private readonly ILogger<CharacterRun> _logger;

    private List<Character> _queue = [];
    private int _index;
    private DateTime _nextTry;
    private DateTime _deadline;
    private readonly AetheryteFunctions _aetheryteFunctions;
    private int _shareTrips;
    private bool _innTried;

    public CharacterRun(Configuration configuration, AutoRetainerIpc autoRetainer, LifestreamIpc lifestream,
        NotificationMasterIpc notifications, QuestController questController, QuestStarter questStarter,
        ConfigShare configShare, AetheryteFunctions aetheryteFunctions, ICommandManager commandManager, QuestStopPolicy stopPolicy, IClientState clientState, IObjectTable objectTable, ICondition condition,
        IFramework framework, IChatGui chatGui, ILogger<CharacterRun> logger)
    {
        _configuration = configuration;
        _autoRetainer = autoRetainer;
        _lifestream = lifestream;
        _notifications = notifications;
        _questController = questController;
        _questStarter = questStarter;
        _configShare = configShare;
        _aetheryteFunctions = aetheryteFunctions;
        _commandManager = commandManager;
        _stopPolicy = stopPolicy;
        _clientState = clientState;
        _objectTable = objectTable;
        _condition = condition;
        _framework = framework;
        _chatGui = chatGui;
        _logger = logger;

        _stopPolicy.StopHandler = OnStop;
        _framework.Update += Update;
    }

    public EState State { get; private set; }

    public bool IsActive => State != EState.Idle;

    public bool CanStart => !IsActive && !_questController.IsRunning &&
                            _questController.AutomationType == EAutomationType.Manual;

    public string? Status
    {
        get
        {
            if (!IsActive)
                return null;

            Character character = _queue[_index];
            string step = State switch
            {
                EState.Questing => _L("questing"),
                EState.Leaving => _L("waiting to switch"),
                EState.Switching => _L("switching"),
                _ => _L("starting"),
            };
            return _LF("Character list: {0}@{1} ({2}/{3}), {4}", character.Name, character.World, _index + 1,
                _queue.Count, step);
        }
    }

    public bool StartsList => !IsActive && CurrentId is { } id && _configuration.Characters.EnabledIds.Contains(id) &&
                              _autoRetainer.IsReady();

    internal static ulong? CurrentId =>
        ulong.TryParse(ProfileController.CurrentContentId, CultureInfo.InvariantCulture, out ulong id) ? id : null;

    internal static List<Character> GetQueue(IReadOnlyList<Character> characters, IReadOnlyCollection<ulong> ticked,
        ulong? current)
    {
        List<Character> queue = characters.Where(x => ticked.Contains(x.ContentId)).ToList();
        int start = queue.FindIndex(x => x.ContentId == current);
        return start <= 0 ? queue : [.. queue.Skip(start), .. queue.Take(start)];
    }

    internal static int? NextIndex(bool canContinue, int index, int count) =>
        canContinue && index + 1 < count ? index + 1 : null;

    public void Start()
    {
        if (!CanStart)
            return;

        if (!_autoRetainer.IsReady())
        {
            PrintError(_L("AutoRetainer is needed to run on several characters."));
            return;
        }

        if (_lifestream.GetVersion() == null)
        {
            PrintError(_L("Lifestream is needed to switch characters."));
            return;
        }

        List<Character> queue = GetQueue(_autoRetainer.GetCharacters(), _configuration.Characters.EnabledIds, CurrentId);
        if (queue.Count == 0)
        {
            PrintError(_L("Tick at least one character on the Profiles page first."));
            return;
        }

        _queue = queue;
        _index = 0;
        _stopPolicy.IsListRun = true;
        _logger.LogInformation("Character list started: {Characters}",
            string.Join(", ", queue.Select(x => $"{x.Name}@{x.World}")));
        Print(_LF("Character list started with {0} character(s).", queue.Count));

        if (queue[0].ContentId == CurrentId)
        {
            _shareTrips = 0;
            _innTried = false;
            State = EState.Starting;
            _nextTry = DateTime.Now;
            _deadline = DateTime.Now + Timeout;
        }
        else
            Leave();
    }

    public void Cancel()
    {
        if (IsActive)
            End(_L("Character list cancelled."));
    }

    private bool OnStop(bool canContinue)
    {
        if (State != EState.Questing)
            return false;

        if (NextIndex(canContinue, _index, _queue.Count) is not { } next)
        {
            End(canContinue
                ? _L("Character list finished.")
                : _LF("Character list ended on {0}.", _queue[_index].Name));
            return false;
        }

        _index = next;
        Leave();
        return true;
    }

    private void Update(IFramework framework)
    {
        _stopPolicy.Update(DateTime.Now);
        if (State is EState.Idle or EState.Questing)
            return;

        DateTime now = DateTime.Now;
        if (now >= _deadline)
        {
            Fail(_LF("Character list stopped: could not get to {0}.", _queue[_index].Name));
            return;
        }

        if (now < _nextTry)
            return;

        _nextTry = now + RetryTime;
        Character target = _queue[_index];
        switch (State)
        {
            case EState.Leaving:
                // Travel, commands and the wait can outlast one step's time limit.
                if (_stopPolicy.IsEnding || _lifestream.IsBusy)
                {
                    _deadline = now + Timeout;
                    return;
                }

                if (_clientState.IsLoggedIn && !IsPlayerReady())
                    return;

                if (_lifestream.ChangeCharacter(target.Name, target.World))
                {
                    State = EState.Switching;
                    _deadline = now + Timeout;
                }
                break;

            case EState.Switching:
                if (!IsCurrent(target) || !IsPlayerReady())
                    return;

                _shareTrips = 0;
                _innTried = false;
                State = EState.Starting;
                _nextTry = now + SettleTime;
                _deadline = now + Timeout;
                break;

            case EState.Starting:
                if (!IsCurrent(target) || !IsPlayerReady() || !_questController.CanPrepareQuestTasks())
                    break;

                // Shared settings first; Config Share's own steps can take longer than one try.
                if (_configShare.IsBusy || _lifestream.IsBusy)
                {
                    _deadline = now + Timeout;
                    break;
                }

                if (_configShare.Due(target.ContentId) && LeftStartingArea() && !TryApplyShare(target, now))
                    break;

                BeginQuesting();
                break;
        }
    }

    // A new character has no city or inn to go to until it reaches its starting city; it gets the settings on a later run.
    private bool LeftStartingArea() =>
        _aetheryteFunctions.IsAetheryteUnlocked(EAetheryteLocation.Limsa) ||
        _aetheryteFunctions.IsAetheryteUnlocked(EAetheryteLocation.Gridania) ||
        _aetheryteFunctions.IsAetheryteUnlocked(EAetheryteLocation.Uldah);

    internal enum EShareTrip
    {
        Apply,
        Skip,
        HomeWorld,
        Inn,
    }

    // One inn try: Lifestream answers a locked inn in red each time.
    internal static EShareTrip ShareTrip(bool homeWorld, bool sanctuary, int trips, bool innTried) =>
        homeWorld && sanctuary ? EShareTrip.Apply
        : trips >= 3 || innTried ? EShareTrip.Skip
        : homeWorld ? EShareTrip.Inn
        : EShareTrip.HomeWorld;

    // False while the shared settings still need a trip or are being applied.
    private bool TryApplyShare(Character target, DateTime now)
    {
        switch (ShareTrip(_configShare.OnHomeWorld, ConfigShare.InSanctuary, _shareTrips, _innTried))
        {
            case EShareTrip.Apply:
                if (!_configShare.Apply())
                {
                    _configShare.Skip(target.ContentId);
                    return true;
                }

                _deadline = now + Timeout;
                return false;

            case EShareTrip.Skip:
                _configShare.Skip(target.ContentId);
                Print(_LF("Shared settings skipped on {0}: couldn't get to a city or inn on the home world.", target.Name));
                return true;

            case EShareTrip.HomeWorld:
                _commandManager.ProcessCommand("/li");
                break;

            default:
                _lifestream.GoInside(LifestreamIpc.EPropertyType.Inn);
                _innTried = true;
                break;
        }

        _shareTrips++;
        _nextTry = now + SettleTime;
        _deadline = now + Timeout;
        return false;
    }

    private void BeginQuesting()
    {
        // Set first: Start can stop and move on before it returns.
        State = EState.Questing;
        _questStarter.Start("Character list");
#pragma warning disable CA1508 // Start's callbacks can change State.
        if (State == EState.Questing && _questController.AutomationType == EAutomationType.Manual)
#pragma warning restore CA1508
            Fail(_LF("Character list stopped: questing would not start on {0}.", _queue[_index].Name));
    }

    private void Leave()
    {
        DateTime now = DateTime.Now;
        State = EState.Leaving;
        _nextTry = now + SettleTime;
        _deadline = now + Timeout;
    }

    private static bool IsCurrent(Character character) => CurrentId == character.ContentId;

    private bool IsPlayerReady() =>
        _clientState.IsLoggedIn && _objectTable[0] != null &&
        !_condition[ConditionFlag.BetweenAreas] && !_condition[ConditionFlag.BetweenAreas51];

    private void Fail(string message)
    {
        _logger.LogWarning("Character list failed in state {State}: {Message}", State, message);
        End(null);
        PrintError(message);
        _notifications.NotifyOnFailure(message);
    }

    private void End(string? message)
    {
        State = EState.Idle;
        _queue = [];
        _index = 0;
        _stopPolicy.IsListRun = false;
        if (message != null)
            Print(message);
    }

    private void Print(string message) => _chatGui.Print(message, CommandHandler.MessageTag, CommandHandler.TagColor);

    private void PrintError(string message) =>
        _chatGui.PrintError(message, CommandHandler.MessageTag, CommandHandler.TagColor);

    public void Dispose()
    {
        _framework.Update -= Update;
        _stopPolicy.StopHandler = null;
    }
}
