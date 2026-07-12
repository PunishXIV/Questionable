using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Fishing;
using Questionable.Controller.Utils;
using Questionable.Data;
using Questionable.External;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Shared;

internal static class Fish
{
  internal sealed class Factory(IAutoHookIpc autoHookIpc) : ITaskFactory
  {
    public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
    {
      if (step.InteractionType != EInteractionType.Fish)
        yield break;

      if (!autoHookIpc.IsAvailable())
        yield break;

      yield return new Mount.UnmountTask();
      yield return new SwitchClassJob.Task(Job.FSH);

      // Ensure we have at least one fish task. Quests that need key items (e.g. And Thanks for All the Fish) do not have itemIds and so will have no requested items.
      yield return new FishTask(quest, step.ItemsToGather.FirstOrDefault(), step.CompletionQuestVariablesFlags);

      // Create additional fish tasks for any additional items.
      foreach (GatheredItem item in step.ItemsToGather.Skip(1))
        yield return new FishTask(quest, item, step.CompletionQuestVariablesFlags);
    }
  }

  internal sealed record FishTask
  (
      Quest Quest,
      GatheredItem? GatheredItem,
      IList<QuestWorkValue?> CompletionQuestVariablesFlags) : ITask
  {
    public bool HasCompletionQuestVariablesFlags { get; } =
        QuestWorkUtils.HasCompletionFlags(CompletionQuestVariablesFlags);

    public override string ToString() =>
        $"Fish{(HasCompletionQuestVariablesFlags ? "*" : "")}{(GatheredItem != null ? $"({GatheredItem.ItemCount}x {GatheredItem.ItemId})" : "")}";
  }

  internal sealed class DoFish(
      IAutoHookIpc autoHookIpc,
      ICommandManager commandManager,
      ICondition condition,
      GameFunctions gameFunctions,
      IChatGui chatGui,
      SendNotification.Executor sendNotificationExecutor,
      IFishingPresetGenerator fishingPresetGenerator,
      ILogger<DoFish> logger) : TaskExecutor<FishTask>, IStoppableTaskExecutor
  {
    private readonly bool _wasAutoHookEnabled = autoHookIpc.IsPluginEnabled();
    private bool _started;
    private bool _cleanupDone;

    protected override bool Start()
    {
      if (HasRequestedItem(Task.GatheredItem))
      {
        logger.LogInformation($"Already have {Task.GatheredItem!.ItemCount}x {Task.GatheredItem.ItemId} in inventory", Task.GatheredItem.ItemCount, Task.GatheredItem.ItemId);
        return false;
      }

      if (HasMatchingCompletionQuestWork())
      {
        logger.LogInformation("Quest variables already match, skipping fish task.");
        return false;
      }

      if (!_wasAutoHookEnabled)
      {
        // AutoHook is required for this task to work. Enable it if it's not already enabled.
        var canEnableAutoHook = autoHookIpc.SetPluginEnabled(enabled: true);
        if (!canEnableAutoHook)
        {
          const string errorText =
              "AutoHook is required for fishing but could not be enabled. Please install or enable AutoHook.";
          logger.LogWarning("{ErrorText}", errorText);
          if (!sendNotificationExecutor.Start(new SendNotification.Task(EInteractionType.Fish, errorText)))
            chatGui.PrintError(errorText, CommandHandler.MessageTag, CommandHandler.TagColor);
          throw new TaskException(errorText);
        }
      }

      // Only create and select the anonymous preset if we haven't started yet. This prevents us from creating multiple presets.
      if (!_started)
      {
        logger.LogDebug("Starting fish task for quest {QuestId}.", Task.Quest.Id);

        if (!FishingData.FishingPresets.TryGetValue((QuestId)Task.Quest.Id, out string? presetExport))
        {
          logger.LogDebug("No fishing preset found for quest {QuestId}. Autocreating from quest data.", Task.Quest.Id);

          presetExport = fishingPresetGenerator.CreatePresetFromTask(Task);
          logger.LogDebug(presetExport);
        }

        // Using an anonymouse preset allows us to easily remove it later.
        logger.LogInformation("Creating and selecting anonymous AutoHook preset for quest {QuestId}", Task.Quest.Id);
        autoHookIpc.CreateAndSelectAnonymousPreset(presetExport);
      }

      // Start fishing via command
      // Native command: gameFunctions.UseAction(EAction.FSHCast);
      logger.LogDebug("Starting fishing");
      commandManager.ProcessCommand("/ahstart");

      _started = true;
      return true;
    }

    public override ETaskResult Update()
    {
      if (HasRequestedItem(Task.GatheredItem))
      {
        logger.LogDebug("Requested item collected. Completing task.");
        Cleanup();
        return ETaskResult.TaskComplete;
      }

      if (HasMatchingCompletionQuestWork())
      {
        logger.LogDebug("Quest variables match. Completing task.");
        Cleanup();
        return ETaskResult.TaskComplete;
      }

      if (EzThrottler.Throttle("FishStart", 1000) && !condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Gathering])
      {
        logger.LogDebug("We don't seem to be fishing. Trying again.");
        Start();
        return ETaskResult.StillRunning;
      }
      return ETaskResult.StillRunning;
    }

    public void StopNow()
    {
      if (_started)
        Cleanup();
    }

    // we're on a gathering class, so combat doesn't make much sense (we also can't change classes in combat...)
    public override bool ShouldInterruptOnDamage() => false;

    private bool HasMatchingCompletionQuestWork()
    {
      if (!Task.HasCompletionQuestVariablesFlags)
        return false;

      QuestProgressInfo? questWork = QuestFunctions.GetQuestProgressInfo(Task.Quest.Id);
      return questWork != null &&
             QuestWorkUtils.MatchesQuestWork(Task.CompletionQuestVariablesFlags, questWork);
    }

    private void Cleanup()
    {
      if (_cleanupDone)
        return;

      logger.LogDebug("Cleaning up fish task.");

      // Make sure we're not fishing anymore.
      gameFunctions.UseAction(EAction.FSHQuit);

      // Clean up anonymous preset
      autoHookIpc.DeleteAllAnonymousPresets();

      // Respect player's current settings. Set plugin to the state it was in at the start.
      autoHookIpc.SetPluginEnabled(_wasAutoHookEnabled);

      _cleanupDone = true;
      _started = false;
    }
  }

  private static unsafe bool HasRequestedItem(GatheredItem? item)
  {
    if (item == null)
      return false;

    InventoryManager* inventoryManager = InventoryManager.Instance();
    if (inventoryManager == null)
      return false;

    return inventoryManager->GetInventoryItemCount(item.ItemId,
        minCollectability: (short)item.Collectability) >= item.ItemCount;
  }
}