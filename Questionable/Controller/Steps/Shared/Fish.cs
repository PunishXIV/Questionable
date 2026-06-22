using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
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
        return [];

      return [
        new Mount.UnmountTask(),
        .. step.ItemsToGather.Select(x => new FishTask(quest, x))
      ];
    }
  }

  internal sealed record FishTask
  (
      Quest Quest,
      GatheredItem GatheredItem) : ITask
  {
    public override string ToString() => $"Fish({GatheredItem.ItemCount}x {GatheredItem.ItemId})";
  }

  internal sealed class DoFish(AutoHookIpc autoHookIpc, ICommandManager commandManager, GameFunctions gameFunctions, ILogger<DoFish> logger) : TaskExecutor<FishTask>
  {
    private readonly bool _wasAutoHookEnabled = autoHookIpc.IsPluginEnabled();

    protected override bool Start()
    {
      if (!_wasAutoHookEnabled)
      {
        // AutoHook is required for this task to work. Enable it if it's not already enabled.
        var canEnableAutoHook = autoHookIpc.SetPluginEnabled(true);
        if (!canEnableAutoHook)
        {
          // ?: If we can't enable AutoHook, how do we send a "manual intervention" notification to the player?
          return false;
        }
      }

      logger.LogInformation("Starting fish task for quest {QuestId}. ItemId: {ItemId}, ItemCount: {ItemCount}", Task.Quest.Id, Task.GatheredItem.ItemId, Task.GatheredItem.ItemCount);

      if (!FishingData.FishingPresets.TryGetValue((QuestId)Task.Quest.Id, out string? presetExport))
      {
        logger.LogInformation("No fishing preset found for quest {QuestId}", Task.Quest.Id);
        return false;
      }

      // Disabled: Using IPC to add and set the preset didn't work.
      // Players must now import the required preset into AutoHook manually.
      // Preset name is the quest ID.
      // TODO: A Class Quests folder preset will be made available from the AutoHook Wiki.
      // // Using an anonymouse preset allows us to easily remove it later.
      // autoHookIpc.CreateAndSelectAnonymousPreset(presetExport);

      // Workaround: Select preset via slash command instead of using IPC
      // AutoHookIpc doesn't tell us if the preset was set successfully, only if the command was found and dispatched.
      // We can see a chat message in game. I don't know if it's possible to check for "Preset not found" or "Preset set to: {presetName}"
      // IPC command: autoHookIpc.SetPreset(Task.Quest.Id.ToString());
      // Select preset via command instead of using IPC
      logger.LogInformation("Selecting preset {Preset} via command", Task.Quest.Id);
      commandManager.ProcessCommand($"/ahpreset {Task.Quest.Id}");

      // Start fishing via command
      // Native command: gameFunctions.UseAction(EAction.FSHCast);
      logger.LogInformation("Starting fishing via command");
      commandManager.ProcessCommand("/ahstart");

      return true;
    }

    public override ETaskResult Update()
    {
      if (HasRequestedItems())
      {
        // Disabled: Using IPC to add and set the preset didn't work.
        // // Clean up anonymous preset
        // autoHookIpc.DeleteAllAnonymousPresets();

        gameFunctions.UseAction(EAction.FSHQuit);

        // Respect player's current settings. Set plugin to the state it was in at the start.
        autoHookIpc.SetPluginEnabled(_wasAutoHookEnabled);

        return ETaskResult.TaskComplete;
      }

      return ETaskResult.StillRunning;
    }

    // we're on a gathering class, so combat doesn't make much sense (we also can't change classes in combat...)
    public override bool ShouldInterruptOnDamage() => false;

    // Shamelessly stolen from Gather.cs
    // Should this be moved to a shared class?
    // Should we try to integrate fishing more closely with gathering? I think they are distinct enough due to the other gathering jobs relying on GatheringPoints. Making them optional for just fishing would be a pain.
    public unsafe bool HasRequestedItems()
    {
      InventoryManager* inventoryManager = InventoryManager.Instance();
      if (inventoryManager == null)
        return false;

      return inventoryManager->GetInventoryItemCount(Task.GatheredItem.ItemId,
          minCollectability: (short)Task.GatheredItem.Collectability) >= Task.GatheredItem.ItemCount;
    }
  }
}