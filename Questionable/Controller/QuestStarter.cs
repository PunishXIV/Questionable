using Questionable.Controller.Steps.Shared;
using EAutomationType = Questionable.Controller.QuestController.EAutomationType;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class QuestStarter(
    QuestController questController,
    QuestProgressTracker tracker,
    QuestData questData,
    QuestStopPolicy stopPolicy,
    JobGear.Factory jobGear,
    SatisfyIpc satisfy,
    Configuration configuration,
    IDataManager dataManager,
    IChatGui chatGui,
    ILogger<QuestStarter> logger)
{
    private ElementId? _single;

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
        stopPolicy.CancelEnding();
        _single = null;
        questController.NextInChain = null;
        questController.AutomationType = EAutomationType.Automatic;
        questController.ExecuteNextStep();
    }

    // Play after a stopped single-quest run (IPC event quests) continues only that quest or chain, not the lists.
    public bool ResumesSingle => questController.NextInChain != null ||
        _single is { } id && id.Equals(questController.CurrentQuest?.Quest.Id);

    public bool TryResume(string label)
    {
        Func<Quest?>? chain = questController.NextInChain;
        if (chain?.Invoke() is { } next)
            questController.SetNextQuest(next);
        else
            // The event quests were finished some other way.
            chain = questController.NextInChain = null;

        if (chain == null && !(_single is { } id && id.Equals(questController.CurrentQuest?.Quest.Id)))
            return false;
        if (StartSingleQuest(label))
            questController.NextInChain = chain;
        return true;
    }

    public bool StartEventQuests(Quest first, Func<Quest?> next, string label)
    {
        questController.SetNextQuest(first);
        if (!StartSingleQuest(label))
            return false;
        questController.NextInChain = next;
        return true;
    }

    public bool StartSingleQuest(string label)
    {
        if (!CanStartQuest(label))
            return false;

        using IDisposable? scope = logger.BeginScope($"SQ/{label}");
        RedeemRewardItems.ResetAttemptedItems();
        stopPolicy.CancelEnding();
        _single = (questController.NextQuest ?? questController.CurrentQuest)?.Quest.Id;
        questController.NextInChain = null;
        questController.AutomationType = EAutomationType.SingleQuestA;
        questController.ExecuteNextStep();
        return true;
    }

    public void StartSingleStep(string label)
    {
        if (!CanStartQuest(label))
            return;

        using IDisposable? scope = logger.BeginScope($"SS/{label}");
        RedeemRewardItems.ResetAttemptedItems();
        stopPolicy.CancelEnding();
        questController.AutomationType = EAutomationType.Manual;
        questController.ExecuteNextStep();
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

    public void RedeemRewards(bool dry = false)
    {
        if (questController.IsRunning)
        {
            chatGui.PrintError(_L("Stop questing first."), CommandHandler.MessageTag, CommandHandler.TagColor);
            return;
        }

        RedeemRewardItems.ResetAttemptedItems();
        stopPolicy.CancelEnding();
        List<ITask> tasks = RedeemRewardItems.CreateTasks(questData, dataManager, overrideConfig: true);
        if (tasks.Count == 0)
        {
            chatGui.Print(_L("Nothing to redeem."), CommandHandler.MessageTag, CommandHandler.TagColor);
            return;
        }

        if (!dry)
            foreach (ITask task in tasks)
                questController.UnsafeEnqueueManualTask(task);
        chatGui.Print(_LF("Redeeming {0} item(s).", tasks.Count - 1), CommandHandler.MessageTag, CommandHandler.TagColor);
        if (dry)
            foreach (ITask task in tasks)
                if (task is RedeemRewardItems.Task redeemTask)
                    chatGui.Print(redeemTask.ItemReward.Name, CommandHandler.MessageTag, CommandHandler.TagColor);
        logger.LogInformation("Tasks created by redeem: {Tasks}", string.Join(", ", tasks.Select(x => x.ToString())));
    }

    // Satisfy refuses NPCs that are locked or done, and any once the 12 weekly deliveries are used up.
    public void RunCustomDeliveries()
    {
        if (questController.IsRunning)
        {
            chatGui.PrintError(_L("Stop questing first."), CommandHandler.MessageTag, CommandHandler.TagColor);
            return;
        }

        if (!satisfy.IsInstalled)
        {
            chatGui.PrintError(_L("Custom deliveries need the Satisfy plugin (vsatisfy)."), CommandHandler.MessageTag,
                CommandHandler.TagColor);
            return;
        }

        List<uint> npcs = configuration.General.DeliveryNpcs;
        if (npcs.Count == 0)
        {
            chatGui.Print(_L("Tick the NPCs to deliver to on the Custom Deliveries page in Journal Progress."),
                CommandHandler.MessageTag, CommandHandler.TagColor);
            return;
        }

        stopPolicy.CancelEnding();
        foreach (uint npc in npcs.Order())
            questController.UnsafeEnqueueManualTask(new CustomDelivery.Task(npc, configuration.General.DeliveryItemFor(npc)));
        logger.LogInformation("Queued custom deliveries for Satisfy: {Npcs}",
            string.Join(", ", npcs.Select(x => $"{x} {configuration.General.DeliveryItemFor(x)}")));
    }

    public void CollectJobGear(int? level)
    {
        if (questController.IsRunning)
        {
            chatGui.PrintError(_L("Stop questing first."), CommandHandler.MessageTag, CommandHandler.TagColor);
            return;
        }

        if (ProfileController.CurrentContentId is not { } contentId)
            return;

        stopPolicy.CancelEnding();
        List<ITask> tasks = jobGear.CreateTasks(contentId, level, force: true, out string reason);
        if (reason.Length > 0)
            chatGui.Print(reason, CommandHandler.MessageTag, CommandHandler.TagColor);
        foreach (ITask task in tasks)
            questController.UnsafeEnqueueManualTask(task);
        logger.LogInformation("Tasks created by jobgear: {Tasks}", string.Join(", ", tasks.Select(x => x.ToString())));
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
