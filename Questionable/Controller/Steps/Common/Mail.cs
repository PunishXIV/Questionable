using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Steps.Shared;
using static Questionable.Controller.Steps.ITaskExecutor;
using Quest = Questionable.Domain.Quest;
namespace Questionable.Controller.Steps.Common;

// Between quests, has Mogmail claim waiting Mog Station and reward mail at a delivery moogle. Mogmail's own
// Auto Pop setting decides whether claimed items are used afterwards.
internal static class Mail
{
    // Delivery moogles talk through CustomTalk CmnDefMogLetter_00002.
    private const uint MoogleTalk = 720898;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);
    private static DateTime _retryAt = DateTime.MinValue;
    private static HashSet<uint>? _moogleIds;

    // Quest dialogue and yes/no handling stay out of the mailbox while this is set.
    internal static bool Active { get; private set; }

    internal sealed record Moogle(EAetheryteLocation City, uint NpcId, uint TerritoryId, Vector3 Position);

    // From the zones' event layouts. New Gridania and Limsa's Lower Decks have none, so two need the aethernet.
    internal static readonly Moogle[] CityMoogles =
    [
        new(EAetheryteLocation.Limsa, 1003567, 128, new(18.515f, 44.5f, 159.012f)),
        new(EAetheryteLocation.Gridania, 1001174, 133, new(-55.442f, 6.716f, -149.933f)),
        new(EAetheryteLocation.Uldah, 1005989, 130, new(-24.632f, 10.056f, -60.799f)),
    ];

    // The starting city first (1 Limsa, 2 Gridania, 3 Ul'dah), then the others; the first fully unlocked route wins.
    internal static (Moogle Moogle, EAetheryteLocation? Shard)? ChooseRoute(byte startTown,
        Func<Moogle, EAetheryteLocation?> shardFor, Func<Moogle, bool> needsShard, Func<EAetheryteLocation, bool> unlocked)
    {
        foreach (Moogle moogle in CityMoogles.OrderBy(x => Array.IndexOf(CityMoogles, x) + 1 == startTown ? 0 : 1))
        {
            if (!unlocked(moogle.City))
                continue;
            if (!needsShard(moogle))
                return (moogle, null);
            if (shardFor(moogle) is { } shard && unlocked(shard))
                return (moogle, shard);
        }

        return null;
    }

    private static HashSet<uint> MoogleIds(IDataManager dataManager) =>
        _moogleIds ??= dataManager.GetExcelSheet<ENpcBase>()
            .Where(x => x.ENpcData.Any(d => d.RowId == MoogleTalk))
            .Select(x => x.RowId)
            .ToHashSet();

    internal sealed class Factory(
        Configuration configuration,
        MogmailIpc mogmail,
        QuestFunctions questFunctions,
        AetheryteFunctions aetheryteFunctions,
        AetheryteData aetheryteData,
        GameFunctions gameFunctions,
        IQuestGameState gameState,
        IClientState clientState,
        ICondition condition,
        IObjectTable objectTable,
        IDataManager dataManager,
        QuestPriorityManager priorityManager,
        ILogger<Factory> logger) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (!configuration.General.ClaimMail || DateTime.Now < _retryAt || MogmailIpc.RewardLetters is not > 0 ||
                !BetweenQuests.CanDetour(quest, step, questFunctions, condition, gameFunctions, priorityManager) || !mogmail.IsInstalled)
                return [];

            HashSet<uint> moogleIds = MoogleIds(dataManager);
            if (objectTable.FirstOrDefault(x => moogleIds.Contains(x.BaseId)) is { } nearby)
            {
                logger.LogInformation("Claiming {Count} reward letters at the delivery moogle in this zone",
                    MogmailIpc.RewardLetters);
                return Claim(new Moogle(EAetheryteLocation.None, nearby.BaseId, clientState.TerritoryType, nearby.Position), null);
            }

            if (ChooseRoute(gameState.StartTown, ShardFor, NeedsShard, aetheryteFunctions.IsAetheryteUnlocked) is not
                { } route)
            {
                logger.LogInformation("Reward mail is waiting, but no delivery moogle can be reached yet");
                _retryAt = DateTime.Now + RetryAfter;
                return [];
            }

            logger.LogInformation("Claiming {Count} reward letters at the delivery moogle in {City}",
                MogmailIpc.RewardLetters, route.Moogle.City);
            return Claim(route.Moogle, route.Shard);
        }

        private bool NeedsShard(Moogle moogle) => aetheryteData.TerritoryIds[moogle.City] != moogle.TerritoryId;

        private EAetheryteLocation? ShardFor(Moogle moogle) =>
            aetheryteData.TerritoryIds
                .Where(x => x.Value == moogle.TerritoryId && x.Key.IsAethernetShard() &&
                            aetheryteData.Locations.ContainsKey(x.Key))
                .Select(x => (EAetheryteLocation?)x.Key)
                .MinBy(x => Vector3.Distance(aetheryteData.Locations[x!.Value], moogle.Position));

        private List<ITask> Claim(Moogle moogle, EAetheryteLocation? shard)
        {
            QuestStep step = new(EInteractionType.Interact, moogle.NpcId, moogle.Position, moogle.TerritoryId)
            {
                StopDistance = 3
            };
            List<ITask> tasks = [];
            if (clientState.TerritoryType != moogle.TerritoryId && moogle.City != EAetheryteLocation.None)
            {
                uint cityTerritory = aetheryteData.TerritoryIds[moogle.City];
                if (clientState.TerritoryType != cityTerritory)
                    tasks.Add(new AetheryteShortcut.Task(step, null, moogle.City, cityTerritory));
                if (shard is { } to)
                    tasks.Add(new Shared.AethernetShortcut.Task(moogle.City, to));
            }

            tasks.Add(new MoveTask(step, moogle.Position));
            tasks.Add(new Take(moogle.NpcId));
            tasks.Add(new Done());
            return tasks;
        }
    }

    internal sealed record Take(uint NpcId) : ITask
    {
        public override string ToString() => "ClaimMail";
    }

    internal sealed class TakeExecutor(
        MogmailIpc mogmail,
        GameFunctions gameFunctions,
        ICondition condition,
        IChatGui chatGui,
        ILogger<TakeExecutor> logger) : TaskExecutor<Take>, IStoppable
    {
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(2);
        // The letter list loads after the mailbox opens; an early claim finds nothing to take.
        private static readonly TimeSpan ListLoadTime = TimeSpan.FromSeconds(5);
        private DateTime _giveUpAt;
        private DateTime _nextTry;
        private DateTime? _openedAt;
        private bool _claiming;
        private int _idleChecks;

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
                StopMogmail();
                Finish();
                throw new TaskException($"Mogmail didn't finish claiming within {GiveUpAfter.TotalMinutes} minutes");
            }

            if (DateTime.Now < _nextTry)
                return ETaskResult.StillRunning;
            _nextTry = DateTime.Now.AddSeconds(1);

            if (_claiming)
                return WaitForMogmail();

            if (mogmail.IsMailboxOpen)
            {
                _openedAt ??= DateTime.Now;
                if (mogmail.ClaimAndDelete())
                {
                    logger.LogInformation("Mogmail is claiming the mail");
                    _claiming = true;
                }
                else if (DateTime.Now - _openedAt > ListLoadTime)
                {
                    logger.LogInformation("Mogmail found nothing to claim");
                    _claiming = true;
                }

                return ETaskResult.StillRunning;
            }

            // TextAdvance moves the moogle's talk along.
            if (condition[ConditionFlag.OccupiedInEvent] || condition[ConditionFlag.OccupiedInQuestEvent])
                return ETaskResult.StillRunning;

            if (condition[ConditionFlag.Mounted])
            {
                gameFunctions.Unmount();
                return ETaskResult.StillRunning;
            }

            if (gameFunctions.FindObjectByDataId(Task.NpcId) is { } moogle)
            {
                gameFunctions.InteractWith(moogle);
                _nextTry = DateTime.Now.AddSeconds(2);
            }

            return ETaskResult.StillRunning;
        }

        // Auto Pop starts right after the claim ends, so Mogmail must stay idle for two checks in a row.
        private ETaskResult WaitForMogmail()
        {
            if (mogmail.IsBusy)
            {
                _idleChecks = 0;
                return ETaskResult.StillRunning;
            }

            if (++_idleChecks < 2 || CloseMailbox())
                return ETaskResult.StillRunning;

            Finish();
            logger.LogInformation("{Count} reward letters left after claiming", MogmailIpc.RewardLetters);
            if (MogmailIpc.RewardLetters is > 0)
            {
                _retryAt = DateTime.Now + RetryAfter;
                chatGui.Print(_L("Some reward mail is still waiting, maybe because your bags are full. Trying again in 30 minutes."),
                    CommandHandler.MessageTag, CommandHandler.TagColor);
            }

            return ETaskResult.TaskComplete;
        }

        public void StopNow()
        {
            StopMogmail();
            Finish();
        }

        // Only a run QST started; Mogmail's own runs are left alone.
        private void StopMogmail()
        {
            if (_claiming && mogmail.IsBusy && mogmail.Stop())
                logger.LogInformation("Stopped Mogmail's claim");
        }

        private static void Finish()
        {
            Active = false;
            CloseMailbox();
        }

        // Returns whether a mail window was still open.
        private static unsafe bool CloseMailbox()
        {
            bool open = false;
            foreach (uint addonId in new[] { AgentLetterView.Instance()->GetAddonId(), AgentLetter.Instance()->GetAddonId() })
            {
                if (addonId == 0)
                    continue;
                AtkUnitBase* addon = AtkStage.Instance()->RaptureAtkUnitManager->GetAddonById((ushort)addonId);
                if (addon == null || !addon->IsVisible)
                    continue;
                addon->FireCallbackInt(-1);
                open = true;
            }

            return open;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record Done : IOptionalGroupEnd
    {
        public bool RetryStep => true;

        public void OnFailed(Exception exception)
        {
            Active = false;
            _retryAt = DateTime.Now + RetryAfter;
            Svc.Chat.Print(_LF("Couldn't claim your reward mail: {0}. Carrying on; trying again in 30 minutes.",
                exception.Message.TrimEnd('.')), CommandHandler.MessageTag, CommandHandler.TagColor);
        }

        public override string ToString() => "ClaimMailDone";
    }

    // Plans the accept step again, which walks back to the quest giver.
    internal sealed class DoneExecutor : TaskExecutor<Done>
    {
        protected override bool Start() => true;
        public override ETaskResult Update() => ETaskResult.RetryStep;
        public override bool ShouldInterruptOnDamage() => false;
    }
}
