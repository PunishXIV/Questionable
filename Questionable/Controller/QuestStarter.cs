using ECommons.ExcelServices;
using Questionable.Controller.Steps.Shared;
using EAutomationType = Questionable.Controller.QuestController.EAutomationType;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class QuestStarter(
    QuestController questController,
    QuestProgressTracker tracker,
    QuestRegistry questRegistry,
    QuestData questData,
    IChatGui chatGui,
    ILogger<QuestStarter> logger)
{
    private const uint HighQualityItemIdOffset = 1_000_000;

    private const uint CollectableItemIdOffset = 500_000;

    public bool CanStartQuest(string label)
    {
        if (questController.CanPrepareQuestTasks())
            return true;

        string reason = questController.ExternalBlockReason ?? questController.DebugState ??
            _L("Check the external operation before restarting.");
        logger.LogWarning("Quest start {Label} blocked: {Reason}", label, reason);
        chatGui.Print(reason, CommandHandler.MessageTag, CommandHandler.TagColor);
        return false;
    }

    public void Start(string label)
    {
        if (!CanStartQuest(label))
            return;

        using IDisposable? scope = logger.BeginScope($"Q/{label}");
        RedeemRewardItems.ResetAttemptedItems();
        questController.AutomationType = EAutomationType.Automatic;
        questController.ExecuteNextStep();
    }

    public void StartGatheringQuest(string label)
    {
        if (!CanStartQuest(label))
            return;

        using IDisposable? scope = logger.BeginScope($"GQ/{label}");
        RedeemRewardItems.ResetAttemptedItems();
        questController.AutomationType = EAutomationType.GatheringOnly;
        questController.ExecuteNextStep();
    }

    public void StartSingleQuest(string label)
    {
        if (!CanStartQuest(label))
            return;

        using IDisposable? scope = logger.BeginScope($"SQ/{label}");
        RedeemRewardItems.ResetAttemptedItems();
        questController.AutomationType = EAutomationType.SingleQuestA;
        questController.ExecuteNextStep();
    }

    public void StartSingleStep(string label)
    {
        if (!CanStartQuest(label))
            return;

        using IDisposable? scope = logger.BeginScope($"SS/{label}");
        RedeemRewardItems.ResetAttemptedItems();
        questController.AutomationType = EAutomationType.Manual;
        questController.ExecuteNextStep();
    }

    public bool StartGathering(uint npcId, uint itemId, Job classJob, int quantity = 1, ushort collectability = 0)
    {
        if (!CanStartQuest("SatisfactionSupply prepare gathering"))
            return false;

        if (itemId > HighQualityItemIdOffset)
            itemId -= HighQualityItemIdOffset;

        if (itemId >= CollectableItemIdOffset)
            itemId -= CollectableItemIdOffset;

        SatisfactionSupplyInfo info = (SatisfactionSupplyInfo)questData.GetAllByIssuerDataId(npcId)
            .Single(x => x is SatisfactionSupplyInfo);
        if (questRegistry.TryGetQuest(info.QuestId, out Quest? quest))
        {
            QuestSequence sequence = quest.FindSequence(0)!;

            QuestStep switchClassStep = sequence.Steps.Single(x => x.InteractionType == EInteractionType.SwitchClass);
            switchClassStep.TargetClass = classJob switch
            {
                Job.MIN => EExtendedClassJob.Miner,
                Job.BTN => EExtendedClassJob.Botanist,
                var _ => throw new ArgumentOutOfRangeException(nameof(classJob), classJob, message: null)
            };

            QuestStep gatherStep = sequence.Steps.Single(x => x.InteractionType == EInteractionType.Gather);
            gatherStep.ItemsToGather =
            [
                new()
                {
                    ItemId = itemId,
                    ItemCount = quantity,
                    Collectability = collectability
                }
            ];
            questController.SetGatheringQuest(quest);
            StartGatheringQuest("SatisfactionSupply prepare gathering");
            return true;
        }

        chatGui.PrintError($"No associated quest ({info.QuestId}).", "Questionable");
        return false;
    }

    public void SimulateQuest(IQuestInfo? questInfo, byte sequence, int step)
    {
        if (!questController.HasExternalOperation)
            tracker.SimulateQuest(questInfo, sequence, step);
    }

    public void SimulateQuest(Quest? quest, byte sequence, int step)
    {
        if (!questController.HasExternalOperation)
            tracker.SimulateQuest(quest, sequence, step);
    }

    public void StopSimulate()
    {
        if (questController.HasExternalOperation)
            questController.Stop("Stop simulation during external operation");
        if (questController.HasExternalOperation)
            return;
        tracker.StopSimulate();
    }
}
