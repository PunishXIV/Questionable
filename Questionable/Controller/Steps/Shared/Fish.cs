using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Utils;
using Questionable.Data;
using Questionable.External;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Shared;

internal static class Fish
{
  internal sealed class Factory : ITaskFactory
  {
    public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
    {
      if (step.InteractionType != EInteractionType.Fish)
        yield break;

      yield return new Mount.UnmountTask();

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
      AutoHookIpc autoHookIpc,
      ICommandManager commandManager,
      GameFunctions gameFunctions,
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
        var canEnableAutoHook = autoHookIpc.SetPluginEnabled(true);
        if (!canEnableAutoHook)
        {
          // ?: If we can't enable AutoHook, how do we send a "manual intervention" notification to the player?
          logger.LogWarning("Failed to enable AutoHook");
          throw new TaskException("Failed to enable AutoHook");
        }
      }

      logger.LogDebug("Starting fish task for quest {QuestId}.", Task.Quest.Id);

      if (!FishingData.FishingPresets.TryGetValue((QuestId)Task.Quest.Id, out string? presetExport))
      {
        logger.LogWarning("No fishing preset found for quest {QuestId}", Task.Quest.Id);
        throw new TaskException($"No fishing preset found for quest {Task.Quest.Id}");
      }

      // Using an anonymouse preset allows us to easily remove it later.
      logger.LogInformation("Creating and selecting anonymous AutoHook preset for quest {QuestId}", Task.Quest.Id);
      autoHookIpc.CreateAndSelectAnonymousPreset(presetExport);

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
    }
  }

  public static unsafe bool HasRequestedItem(GatheredItem? item)
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