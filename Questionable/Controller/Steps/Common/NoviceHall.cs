using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Quest = Questionable.Domain.Quest;
namespace Questionable.Controller.Steps.Common;

// Between quests, has AutoDuty run the three Hall of the Novice exercises that give the Neophyte's Ring.
// AutoDuty queues them from anywhere, so there is no trip to the Hall.
internal static class NoviceHall
{
    // React to Attack Markers, React to Floor Markers, React to Advanced Visual Indicators.
    internal static readonly uint[] Exercises = [1012, 1013, 1014];
    internal const uint NeophytesRing = 44410;
    internal const int MinLevel = 49;
    // The ring's EXP bonus stops at 60.
    internal const int MaxLevel = 59;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);
    private static DateTime _retryAt = DateTime.MinValue;

    // The game has no readable exercise-done flag; the ring from the last exercise stands in for it.
    internal static bool Wanted(Job job, int level, bool hasRing) =>
        !hasRing && level is >= MinLevel and <= MaxLevel &&
        job is not (Job.ADV or Job.BLU or (>= Job.CRP and <= Job.FSH));

    private static unsafe bool HasRing()
    {
        InventoryManager* inventory = InventoryManager.Instance();
        return inventory != null &&
               inventory->GetInventoryItemCount(NeophytesRing, isHq: false, checkEquipped: true, checkArmory: true) > 0;
    }

    internal sealed class Factory(
        Configuration configuration,
        AutoDutyProvider autoDuty,
        QuestFunctions questFunctions,
        GameFunctions gameFunctions,
        ICondition condition,
        ILogger<Factory> logger) : ITaskFactory
    {
        public unsafe IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (!configuration.General.RunNoviceHall || DateTime.Now < _retryAt ||
                !BetweenQuests.CanDetour(quest, step, questFunctions, condition, gameFunctions))
                return [];

            PlayerState* playerState = PlayerState.Instance();
            if (playerState == null ||
                !Wanted((Job)playerState->CurrentClassJobId, playerState->CurrentLevel, HasRing()))
                return [];

            if (!Exercises.All(x => autoDuty.CanRunDuty(new DutyOptions { ContentFinderConditionId = x, Enabled = true })))
                return [];

            logger.LogInformation("Running the Hall of the Novice exercises for the Neophyte's Ring");
            return
            [
                .. Exercises.Select(x => new Duty.StartAutoDutyTask(x, Duty.Mode.NoviceHall, AllowUnsync: false)),
                new Done(),
            ];
        }
    }

    internal sealed record Done : IOptionalGroupEnd
    {
        public bool RetryStep => true;

        public void OnFailed(Exception exception) =>
            Pause(_LF("Couldn't finish the Hall of the Novice exercises: {0}. Carrying on; trying again in 30 minutes.",
                exception.Message.TrimEnd('.')));

        public override string ToString() => "NoviceHallDone";
    }

    private static void Pause(string message)
    {
        _retryAt = DateTime.Now + RetryAfter;
        Svc.Chat.Print(message, CommandHandler.MessageTag, CommandHandler.TagColor);
    }

    // Plans the accept step again, which walks back to the quest giver.
    internal sealed class DoneExecutor : TaskExecutor<Done>
    {
        protected override bool Start() => true;

        public override ETaskResult Update()
        {
            if (!HasRing())
                Pause(_L("The Hall of the Novice exercises ended without the Neophyte's Ring. Carrying on; trying again in 30 minutes."));
            return ETaskResult.RetryStep;
        }

        public override bool ShouldInterruptOnDamage() => false;
    }
}
