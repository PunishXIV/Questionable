using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Quest = Questionable.Domain.Quest;
namespace Questionable.Controller.Steps.Shared;

internal static class SummonChocobo
{
    internal const float TopUpBelowSeconds = 300;

    private const uint GysahlGreens = 4868;
    private const ushort UnlockQuest = 1162; // My Feisty Little Chocobo
    private const uint Town = 0; // TerritoryIntendedUse
    private const uint OpenWorld = 1; // the companion can't be called in towns or duties
    private const int RestockTo = 99;

    // One try per window, so a place that refuses the summon doesn't cost a dismount on every step.
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);
    private static DateTime _retryAt = DateTime.MinValue;
    private static bool _warnedNoGreens;

    // A failed purchase stops the run with Knightshopper's reason; don't walk into it again right away.
    private static readonly TimeSpan RestockRetryAfter = TimeSpan.FromMinutes(30);
    private static DateTime _restockRetryAt = DateTime.MinValue;

    internal static bool ShouldSummon(bool enabled, bool unlocked, bool openWorld, float timeLeft, int greens) =>
        enabled && unlocked && openWorld && greens > 0 && timeLeft < TopUpBelowSeconds;

    internal static bool ShouldRestock(bool enabled, bool unlocked, bool townOrOpenWorld, int greens, bool canBuy) =>
        enabled && unlocked && townOrOpenWorld && greens == 0 && canBuy;

    internal sealed class Factory(
        Configuration configuration,
        QuestFunctions questFunctions,
        IDataManager dataManager,
        IClientState clientState,
        IChatGui chatGui,
        KnightshopperIpc knightshopperIpc) : ITaskFactory
    {
        public unsafe IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (!configuration.General.SummonChocobo)
                return [];

            InventoryManager* inventoryManager = InventoryManager.Instance();
            UIState* uiState = UIState.Instance();
            if (inventoryManager == null || uiState == null)
                return [];

            bool unlocked = questFunctions.IsQuestComplete(new QuestId(UnlockQuest));
            uint? zoneUse = dataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(clientState.TerritoryType)
                ?.TerritoryIntendedUse.RowId;
            bool openWorld = zoneUse == OpenWorld;
            float timeLeft = uiState->Buddy.CompanionInfo.TimeLeft;
            int greens = inventoryManager->GetInventoryItemCount(GysahlGreens);

            if (greens == 0)
            {
                if (DateTime.Now >= _restockRetryAt &&
                    ShouldRestock(enabled: true, unlocked, openWorld || zoneUse == Town, greens, CanBuy(inventoryManager)))
                {
                    _restockRetryAt = DateTime.Now.Add(RestockRetryAfter);
                    // Knightshopper may leave us in another zone, so the step is planned again afterwards.
                    return [new Purchase.BuyTask(GysahlGreens, RestockTo), new Restocked()];
                }

                if (!_warnedNoGreens && ShouldSummon(enabled: true, unlocked, openWorld, timeLeft, greens: 1))
                {
                    _warnedNoGreens = true;
                    chatGui.Print(_L("You're out of Gysahl Greens, so your chocobo wasn't called. Buy some to keep it out."),
                        CommandHandler.MessageTag, CommandHandler.TagColor);
                }

                return [];
            }

            _warnedNoGreens = false;
            if (DateTime.Now < _retryAt || !ShouldSummon(enabled: true, unlocked, openWorld, timeLeft, greens))
                return [];

            return [new MountStep.UnmountTask(), new Task()];
        }

        private unsafe bool CanBuy(InventoryManager* inventoryManager)
        {
            uint price = dataManager.GetExcelSheet<Item>().GetRowOrDefault(GysahlGreens)?.PriceMid ?? 0;
            return price > 0 && inventoryManager->GetGil() >= price * RestockTo &&
                   knightshopperIpc.IsReady() && knightshopperIpc.GetBusy() == false;
        }
    }

    internal sealed record Restocked : ITask
    {
        public override string ToString() => "PlanStepAgain";
    }

    internal sealed class RestockedExecutor : TaskExecutor<Restocked>
    {
        protected override bool Start() => true;
        public override ETaskResult Update() => ETaskResult.RetryStep;
        public override bool ShouldInterruptOnDamage() => false;
    }

    internal sealed record Task : ITask
    {
        public override string ToString() => "SummonChocobo";
    }

    internal sealed class Executor(
        GameFunctions gameFunctions,
        ICondition condition,
        ILogger<Executor> logger) : TaskExecutor<Task>
    {
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan TryEvery = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan SummonTime = TimeSpan.FromSeconds(3);

        private DateTime _giveUpAt;
        private DateTime _nextTryAt;
        private DateTime? _usedAt;

        protected override bool Start()
        {
            _retryAt = DateTime.Now.Add(RetryAfter);
            _giveUpAt = DateTime.Now.Add(GiveUpAfter);
            _nextTryAt = DateTime.MinValue;
            _usedAt = null;
            return true;
        }

        // Never fails the step: a chocobo that didn't come is not worth stopping for.
        public override ETaskResult Update()
        {
            if (_usedAt is { } usedAt)
                return DateTime.Now < usedAt.Add(SummonTime) ? ETaskResult.StillRunning : ETaskResult.TaskComplete;

            if (DateTime.Now > _giveUpAt)
            {
                logger.LogInformation("Could not use Gysahl Greens in time, carrying on without the chocobo");
                return ETaskResult.TaskComplete;
            }

            if (DateTime.Now < _nextTryAt || condition[ConditionFlag.Mounted] || condition[ConditionFlag.InCombat] ||
                gameFunctions.IsOccupied())
                return ETaskResult.StillRunning;

            _nextTryAt = DateTime.Now.Add(TryEvery);
            if (gameFunctions.UseItem(GysahlGreens))
            {
                logger.LogInformation("Used Gysahl Greens to call the chocobo");
                _usedAt = DateTime.Now;
            }

            return ETaskResult.StillRunning;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
