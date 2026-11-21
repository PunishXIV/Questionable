using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Steps.Shared;
using static Questionable.Controller.Steps.ITaskExecutor;
using Quest = Questionable.Domain.Quest;
namespace Questionable.Controller.Steps.Common;

internal static class Mail
{
    // Delivery moogles talk through CustomTalk CmnDefMogLetter_00002.
    private const uint MoogleTalk = 720898;
    private static readonly BetweenQuests.Backoff Retry = new();
    private static HashSet<uint>? _moogleIds;

    // Quest dialogue and yes/no handling stay out of the mailbox while this is set.
    internal static bool Active { get; private set; }

    internal sealed record Moogle(EAetheryteLocation City, uint NpcId, uint TerritoryId, Vector3 Position);

    // From the zones' event layouts; New Gridania and Limsa's Lower Decks have none.
    internal static readonly Moogle[] CityMoogles =
    [
        new(EAetheryteLocation.Limsa, 1003567, 128, new(18.515f, 44.5f, 159.012f)),
        new(EAetheryteLocation.Gridania, 1001174, 133, new(-55.442f, 6.716f, -149.933f)),
        new(EAetheryteLocation.Uldah, 1005989, 130, new(-24.632f, 10.056f, -60.799f)),
    ];

    // Starting city first (1 Limsa, 2 Gridania, 3 Ul'dah).
    internal static Moogle? ChooseRoute(byte startTown, Func<EAetheryteLocation, bool> unlocked) =>
        CityMoogles.OrderBy(x => Array.IndexOf(CityMoogles, x) + 1 == startTown ? 0 : 1)
            .FirstOrDefault(x => unlocked(x.City));

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
            if (!configuration.General.ClaimMail || Retry.Waiting || MogmailIpc.RewardLetters is not > 0 ||
                !BetweenQuests.CanDetour(quest, step, questFunctions, condition, gameFunctions, priorityManager) || !mogmail.IsInstalled)
                return [];

            HashSet<uint> moogleIds = MoogleIds(dataManager);
            if (objectTable.FirstOrDefault(x => moogleIds.Contains(x.BaseId)) is { } nearby)
            {
                logger.LogInformation("Claiming {Count} reward letters at the delivery moogle in this zone",
                    MogmailIpc.RewardLetters);
                return Claim(new Moogle(EAetheryteLocation.None, nearby.BaseId, clientState.TerritoryType, nearby.Position));
            }

            if (ChooseRoute(gameState.StartTown, aetheryteFunctions.IsAetheryteUnlocked) is not { } moogle)
            {
                logger.LogInformation("Reward mail is waiting, but no delivery moogle can be reached yet");
                Retry.Failed();
                return [];
            }

            logger.LogInformation("Claiming {Count} reward letters at the delivery moogle in {City}",
                MogmailIpc.RewardLetters, moogle.City);
            return Claim(moogle);
        }

        private static List<ITask> Claim(Moogle moogle) =>
            [new Travel.Task(moogle.TerritoryId, moogle.Position, moogle.NpcId), new Take(moogle.NpcId), new Done()];
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
                Retry.Failed();
                chatGui.Print(_L("Some reward mail is still waiting, maybe because your inventory is full. Trying again later."),
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
            Retry.Failed();
            Svc.Chat.Print(_LF("Couldn't claim your reward mail: {0}. Carrying on; trying again later.",
                exception.Message.TrimEnd('.')), CommandHandler.MessageTag, CommandHandler.TagColor);
        }

        public override string ToString() => "ClaimMailDone";
    }

    internal sealed class DoneExecutor : TaskExecutor<Done>
    {
        protected override bool Start() => true;
        public override ETaskResult Update() => ETaskResult.RetryStep;
        public override bool ShouldInterruptOnDamage() => false;
    }
}
