using FFXIVClientStructs.FFXIV.Component.GUI;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Steps.Shared;
using static Questionable.Controller.Steps.ITaskExecutor;
namespace Questionable.Controller.Steps.Common;

// Sends only this character's retainers, the way AutoDuty does: open a bell, let AutoRetainer work, close it.
internal static class SummoningBell
{
    internal sealed record Bell(uint TerritoryId, uint DataId, Vector3 Position);

    // City bells, from AutoDuty's SummoningBellHelper.
    internal static readonly Bell[] Bells =
    [
        new(129, 2000401, new(-123.88806f, 17.990356f, 21.469421f)), // Limsa Lominsa Lower Decks
        new(133, 2000401, new(171.00781f, 15.487854f, -101.487854f)), // Old Gridania
        new(131, 2000401, new(148.91272f, 3.982544f, -44.205383f)), // Ul'dah - Steps of Thal
        new(419, 2000401, new(-151.1712f, -12.64978f, -11.7647705f)), // The Pillars
        new(635, 2000441, new(-57.63336f, -0.015319824f, 49.30188f)), // Rhalgr's Reach
        new(628, 2000441, new(19.394226f, 4.043579f, 53.025024f)), // Kugane
        new(759, 2006565, new(60.56299f, -0.015319824f, -3.982666f)), // The Doman Enclave
        new(819, 2010284, new(-69.840576f, -7.7058716f, 123.49121f)), // The Crystarium
        new(820, 2010284, new(7.1869507f, 83.17688f, 31.448853f)), // Eulmore
        new(962, 2000441, new(42.09961f, 2.517002f, -39.414062f)), // Old Sharlayan
        new(963, 2000441, new(26.749023f, -0.015319824f, -53.696533f)), // Radz-at-Han
        new(1185, 2000441, new(18.57019f, -14.023071f, 120.408936f)), // Tuliyollal
        new(1186, 2000441, new(-151.59845f, 0.59503174f, -15.304871f)), // Solution Nine
    ];

    // Saved in RetainerBell; any other value is a city's territory id.
    internal const uint Nearest = 0;
    internal const uint Inn = 1;
    internal const uint Apartment = 2;
    internal const uint PrivateHouse = 3;
    internal const uint FcHouse = 4;

    // From AutoDuty: the inn room's bell, and the furnishing bell in any house or apartment.
    private const uint InnBell = 2000403;
    private const uint HouseBell = 196630;

    private static readonly BetweenQuests.Backoff Retry = new();

    // Quest dialogue and yes/no handling stay out of AutoRetainer's menus while this is set.
    internal static bool Active { get; private set; }

    internal static bool Waiting => Retry.Waiting;

    // The chosen city's bell; for Nearest the one in this zone, else the one in your grand company's city.
    internal static Bell Pick(uint preference, uint territoryId, byte grandCompany) =>
        Bells.FirstOrDefault(x => x.TerritoryId == preference) ??
        Bells.FirstOrDefault(x => x.TerritoryId == territoryId) ??
        Bells.First(x => x.TerritoryId == grandCompany switch { 2 => 133u, 3 => 131u, _ => 129u });

    internal static List<ITask> Tasks(uint preference, uint territoryId, byte grandCompany)
    {
        if (preference is Inn or Apartment or PrivateHouse or FcHouse)
            return [new Reach(preference), new Send(preference == Inn ? InnBell : HouseBell), new Done()];

        Bell bell = Pick(preference, territoryId, grandCompany);
        return [new Travel.Task(bell.TerritoryId, bell.Position, bell.DataId), new Send(bell.DataId), new Done()];
    }

    internal sealed record Reach(uint Place) : ITask
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"GoToBell({Place})");
    }

    // Lifestream gets you into the inn room or the house; the bell is somewhere inside.
    internal sealed class ReachExecutor(
        LifestreamIpc lifestream,
        GameFunctions gameFunctions,
        ICondition condition) : TaskExecutor<Reach>
    {
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(3);
        private DateTime _giveUpAt;
        private bool _asked;

        private LifestreamIpc.EPropertyType Type => Task.Place switch
        {
            Apartment => LifestreamIpc.EPropertyType.Apartment,
            PrivateHouse => LifestreamIpc.EPropertyType.Home,
            FcHouse => LifestreamIpc.EPropertyType.FC,
            _ => LifestreamIpc.EPropertyType.Inn,
        };

        protected override bool Start()
        {
            _giveUpAt = DateTime.Now + GiveUpAfter;
            if (lifestream.HasProperty(Type) == false)
                throw new TaskException(string.Create(CultureInfo.InvariantCulture, $"Lifestream doesn't know a {Type} for this character"));
            return true;
        }

        public override ETaskResult Update()
        {
            bool moving = lifestream.IsBusy || condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51];
            if (!moving && gameFunctions.FindObjectByDataId(Task.Place == Inn ? InnBell : HouseBell) != null)
                return ETaskResult.TaskComplete;

            if (DateTime.Now > _giveUpAt)
                throw new TaskException(string.Create(CultureInfo.InvariantCulture,
                    $"Lifestream didn't get you to the bell within {GiveUpAfter.TotalMinutes} minutes"));

            if (!moving && !_asked)
            {
                if (!lifestream.GoInside(Type))
                    throw new TaskException("Lifestream isn't available");
                _asked = true;
            }

            return ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record Send(uint DataId) : ITask
    {
        public override string ToString() => "SendRetainers";
    }

    internal sealed class SendExecutor(
        AutoRetainerIpc autoRetainer,
        RetainerPass pass,
        MovementController movementController,
        IObjectTable objectTable,
        GameFunctions gameFunctions,
        ICondition condition,
        ICommandManager commandManager,
        ILogger<SendExecutor> logger) : TaskExecutor<Send>, IStoppable
    {
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);
        // AutoRetainer starts a moment after it's switched on; if it never gets busy there was nothing to do.
        private static readonly TimeSpan StartTime = TimeSpan.FromSeconds(10);

        private DateTime _giveUpAt;
        private DateTime _nextTry;
        private DateTime? _sentAt;
        private bool _seenBusy;
        private int _closeTries;

        protected override bool Start()
        {
            Active = true;
            _giveUpAt = DateTime.Now + GiveUpAfter;
            return true;
        }

        public override ETaskResult Update()
        {
            if (DateTime.Now > _giveUpAt)
            {
                Finish();
                throw new TaskException(string.Create(CultureInfo.InvariantCulture,
                    $"AutoRetainer didn't finish within {GiveUpAfter.TotalMinutes} minutes"));
            }

            if (DateTime.Now < _nextTry)
                return ETaskResult.StillRunning;
            _nextTry = DateTime.Now.AddSeconds(1);

            if (_sentAt != null)
            {
                if (autoRetainer.IsBusy)
                {
                    _seenBusy = true;
                    return ETaskResult.StillRunning;
                }

                if (!_seenBusy && DateTime.Now - _sentAt < StartTime)
                    return ETaskResult.StillRunning;

                if (condition[ConditionFlag.OccupiedSummoningBell] && _closeTries++ < 10)
                {
                    CloseBell();
                    return ETaskResult.StillRunning;
                }

                Finish();
                pass.BellDone();
                return ETaskResult.TaskComplete;
            }

            if (condition[ConditionFlag.OccupiedSummoningBell])
            {
                logger.LogInformation("At the summoning bell; switching AutoRetainer on");
                commandManager.ProcessCommand("/autoretainer e");
                _sentAt = DateTime.Now;
                return ETaskResult.StillRunning;
            }

            if (condition[ConditionFlag.Mounted])
            {
                gameFunctions.Unmount();
                return ETaskResult.StillRunning;
            }

            if (gameFunctions.FindObjectByDataId(Task.DataId) is { } bell)
            {
                if (objectTable[0] is { } player && Vector3.Distance(player.Position, bell.Position) > 3f)
                {
                    if (!movementController.IsPathRunning)
                        movementController.NavigateTo(EMovementType.None, Task.DataId, bell.Position, new() { StopDistance = 2.5f });
                    return ETaskResult.StillRunning;
                }

                gameFunctions.InteractWith(bell);
                _nextTry = DateTime.Now.AddSeconds(2);
            }

            return ETaskResult.StillRunning;
        }

        public void StopNow() => Finish();

        private void Finish()
        {
            Active = false;
            if (_sentAt == null)
                return;

            // AutoDuty switches it off again too; left on, AutoRetainer acts at every bell you open.
            commandManager.ProcessCommand("/autoretainer d");
            _sentAt = null;
            CloseBell();
        }

        private static unsafe void CloseBell()
        {
            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerList", out var list) && list->IsVisible)
                list->FireCallbackInt(-1);
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record Done : IOptionalGroupEnd
    {
        public bool RetryStep => true;

        public void OnFailed(Exception exception)
        {
            Active = false;
            Retry.Failed();
            Svc.Chat.Print(_LF("Couldn't send your retainers: {0}. Carrying on; trying again later.",
                exception.Message.TrimEnd('.')), CommandHandler.MessageTag, CommandHandler.TagColor);
        }

        public override string ToString() => "SendRetainersDone";
    }

    internal sealed class DoneExecutor : TaskExecutor<Done>
    {
        protected override bool Start() => true;
        public override ETaskResult Update() => ETaskResult.RetryStep;
        public override bool ShouldInterruptOnDamage() => false;
    }
}
