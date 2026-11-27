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
    Steps.Common.Mail.Factory mail,
    SatisfyIpc satisfy,
    CustomDeliveries customDeliveries,
    BossModIpc bossMod,
    Configuration configuration,
    IDataManager dataManager,
    IChatGui chatGui,
    ILogger<QuestStarter> logger)
{
    private ElementId? _single;

    public event EventHandler? Started;

    public bool CanStartQuest(string label)
    {
        if (questController.CanPrepareQuestTasks())
        {
            if (bossMod.IsRebornInstalled())
                chatGui.PrintError(_L("Boss Mod Reborn is loaded. Questionable doesn't work with it, so combat and quest battles can fail. Disable Boss Mod Reborn and use Boss Mod instead."),
                    CommandHandler.MessageTag, CommandHandler.TagColor);
            return true;
        }

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
        questController.RedoFinishedSequence();
        _single = null;
        questController.NextInChain = null;
        questController.AutomationType = EAutomationType.Automatic;
        if (stopPolicy.IsListRun && questController.ReachedStopQuest() is { } reached)
        {
            logger.LogInformation("Stop quest {QuestId} already reached, next character", reached.Id);
            chatGui.Print(_LF("This character is already at the stop quest '{0}', moving on.", reached.Name),
                CommandHandler.MessageTag, CommandHandler.TagColor);
            questController.StopFinished($"Stop quest [{reached.Id}] already reached");
            return;
        }

        // Before the first step's tasks are made, so a start-of-questing detour can join them.
        Started?.Invoke(this, EventArgs.Empty);
        questController.ExecuteNextStep();
    }

    public bool ResumesSingle => questController.NextInChain != null ||
        _single is { } id && id.Equals(questController.CurrentQuest?.Quest.Id);

    public bool TryResume(string label)
    {
        Func<Quest?>? chain = questController.NextInChain;
        if (chain?.Invoke() is { } next)
            questController.SetNextQuest(next);
        else
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
        questController.RedoFinishedSequence();
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
        questController.RedoFinishedSequence();
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

    public void ClaimMail()
    {
        if (questController.IsRunning)
        {
            chatGui.PrintError(_L("Stop questing first."), CommandHandler.MessageTag, CommandHandler.TagColor);
            return;
        }

        if (mail.CommandTasks() is not { } tasks)
        {
            chatGui.PrintError(_L("Claiming mail needs Mogmail."), CommandHandler.MessageTag, CommandHandler.TagColor);
            return;
        }

        if (MogmailIpc.RewardLetters is 0)
        {
            chatGui.Print(_L("No mail waiting."), CommandHandler.MessageTag, CommandHandler.TagColor);
            return;
        }

        stopPolicy.CancelEnding();
        foreach (ITask task in tasks)
            questController.UnsafeEnqueueManualTask(task);
        chatGui.Print(_L("Claiming your mail."), CommandHandler.MessageTag, CommandHandler.TagColor);
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
        chatGui.Print(_LF("Redeeming {0} item(s).", RedeemRewardItems.ItemCount(tasks)), CommandHandler.MessageTag, CommandHandler.TagColor);
        if (dry)
            foreach (ITask task in tasks)
                if (task is RedeemRewardItems.Task redeemTask)
                    chatGui.Print(redeemTask.ItemReward.Name, CommandHandler.MessageTag, CommandHandler.TagColor);
        logger.LogInformation("Tasks created by redeem: {Tasks}", string.Join(", ", tasks.Select(x => x.ToString())));
    }

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

        foreach (ScripGain scrip in customDeliveries.ScripGains(customDeliveries.GetNpcs(), configuration.General))
            if (scrip.Lost > 0)
                chatGui.PrintError(_LF("These deliveries give up to {0} {1}; {2} would be lost to the cap.", scrip.Gain,
                    scrip.Name, scrip.Lost), CommandHandler.MessageTag, CommandHandler.TagColor);

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
