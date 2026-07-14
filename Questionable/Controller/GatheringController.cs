using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Gathering;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Movement;
using Questionable.Data;
using Questionable.External;
using Questionable.Functions;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;
using Mount = Questionable.Controller.Steps.Common.Mount;

namespace Questionable.Controller;

internal sealed unsafe class GatheringController
(
    MovementController movementController,
    GatheringPointRegistry gatheringPointRegistry,
    NavmeshIpc navmeshIpc,
    IObjectTable objectTable,
    IChatGui chatGui,
    ILogger<GatheringController> logger,
    ICondition condition,
    IServiceProvider serviceProvider,
    InterruptHandler interruptHandler,
    IDataManager dataManager,
    IPluginLog pluginLog) : MiniTaskController<GatheringController>(chatGui, condition, serviceProvider, interruptHandler, dataManager, logger)
{
    public enum EStatus
    {
        Gathering,
        Moving,
        Complete
    }

    private readonly ICondition _condition = condition;
    private readonly ILogger<GatheringController> _logger = logger;
    private readonly Regex _revisitRegex = DataManagerAdapter.GetRegex<LogMessage>(dataManager, 5574, x => x.Text, pluginLog)
                                           ?? throw new InvalidDataException("No regex found for revisit message");

    private CurrentRequest? _currentRequest;

    public bool Start(GatheringRequest gatheringRequest)
    {
        if (!gatheringPointRegistry.TryGetGatheringPoint(gatheringRequest.GatheringPointId,
            out GatheringRoot? gatheringRoot))
        {
            _logger.LogError("Unable to resolve gathering point, no path found for {ItemId} / point {PointId}",
                gatheringRequest.ItemId, gatheringRequest.GatheringPointId);
            return false;
        }

        _currentRequest = new()
        {
            Data = gatheringRequest,
            Root = gatheringRoot,
            Nodes = gatheringRoot.Groups
                // at least in EW-ish, there's one node with 1 fixed location and one node with 3 random locations
                .SelectMany(x => x.Nodes.OrderBy(y => y.Locations.Count))
                .ToList()
        };

        if (HasRequestedItems())
        {
            _currentRequest = null;
            return false;
        }

        return true;
    }

    public EStatus Update()
    {
        if (_currentRequest == null)
        {
            Stop("No request");
            return EStatus.Complete;
        }

        if (movementController.IsPathfinding || movementController.IsPathRunning)
            return EStatus.Moving;

        if (HasRequestedItems() && !_condition[ConditionFlag.Gathering])
        {
            Stop("Has all items");
            return EStatus.Complete;
        }

        if (_taskQueue.AllTasksComplete)
            GoToNextNode();

        UpdateCurrentTask();
        return EStatus.Gathering;
    }

    protected override void OnTaskComplete(ITask task) => GoToNextNode();

    public override void Stop(string label)
    {
        _currentRequest = null;
        _taskQueue.Reset();
    }

    public override void Dispose()
    {
        base.Dispose();
        movementController.Dispose();
        gatheringPointRegistry.Dispose();
    }

    private void GoToNextNode()
    {
        if (_currentRequest == null)
            return;

        if (!_taskQueue.AllTasksComplete)
            return;

        GatheringNode? currentNode = FindNextTargetableNodeAndUpdateIndex(_currentRequest);
        if (currentNode == null)
            return;

        if (_currentRequest.Root.Steps.Count == 0 || currentNode.Locations.Count == 0)
        {
            _logger.LogWarning("Gathering request has no steps or current node has no locations; aborting");
            Stop("Empty gathering plan");
            return;
        }

        uint territoryId = _currentRequest.Root.Steps[^1].TerritoryId;
        bool fly = currentNode.Fly.GetValueOrDefault(_currentRequest.Root.FlyBetweenNodes.GetValueOrDefault(defaultValue: true)) &&
                   GameFunctions.IsFlyingUnlocked(territoryId);
        if (currentNode.Locations.Count > 1)
        {
            Vector3 averagePosition = new()
            {
                X = currentNode.Locations.Sum(x => x.Position.X) / currentNode.Locations.Count,
                Y = currentNode.Locations.Select(x => x.Position.Y).Max() + 5f,
                Z = currentNode.Locations.Sum(x => x.Position.Z) / currentNode.Locations.Count
            };

            Vector3? pointOnFloor = navmeshIpc.GetPointOnFloor(averagePosition, unlandable: true);
            if (pointOnFloor != null)
                pointOnFloor = pointOnFloor.Value with { Y = pointOnFloor.Value.Y + (fly ? 3f : 0f) };

            Vector3 moveTarget = pointOnFloor ?? averagePosition;
            Vector3? playerPos = objectTable[0]?.Position;
            if (playerPos == null || Vector3.Distance(playerPos.Value, moveTarget) > 50f)
                _taskQueue.Enqueue(new Mount.MountTask(territoryId, Mount.EMountIf.Always));

            _taskQueue.Enqueue(new MoveTask(territoryId, moveTarget,
                Mount: null, 50f, Fly: fly, IgnoreDistanceToObject: true, InteractionType: EInteractionType.WalkTo));
        }
        else
        {
            Vector3 targetPosition = currentNode.Locations[0].Position;
            Vector3? playerPos = objectTable[0]?.Position;
            if (playerPos == null || Vector3.Distance(playerPos.Value, targetPosition) > 50f)
                _taskQueue.Enqueue(new Mount.MountTask(territoryId, Mount.EMountIf.Always));
        }

        _taskQueue.Enqueue(new MoveToLandingLocation.Task(territoryId, fly, currentNode));
        _taskQueue.Enqueue(new Mount.UnmountTask());
        _taskQueue.Enqueue(new Interact.Task(currentNode.DataId, Quest: null, EInteractionType.Gather, SkipMarkerCheck: true));

        QueueGatherNode(currentNode);
    }

    private void QueueGatherNode(GatheringNode currentNode)
    {
        foreach (bool revisitRequired in new[] { false, true })
        {
            _taskQueue.Enqueue(new DoGather.Task(_currentRequest!.Data, currentNode, revisitRequired));
            if (_currentRequest.Data.Collectability > 0)
                _taskQueue.Enqueue(new DoGatherCollectable.Task(_currentRequest.Data, currentNode, revisitRequired));
        }
    }

    public bool HasRequestedItems()
    {
        if (_currentRequest == null)
            return true;

        InventoryManager* inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return false;

        return inventoryManager->GetInventoryItemCount(_currentRequest.Data.ItemId,
            minCollectability: (short)_currentRequest.Data.Collectability) >= _currentRequest.Data.Quantity;
    }

    public bool HasNodeDisappeared(GatheringNode node)
    {
        return !objectTable.Any(x =>
            x.ObjectKind == ObjectKind.GatheringPoint && x.IsTargetable && GameFunctions.GetBaseID(x) == node.DataId);
    }

    /// <summary>
    ///     For leves in particular, there's a good chance you're close enough to all nodes in the next group
    ///     but none are targetable (if they're not part of the randomly-picked route).
    /// </summary>
    private GatheringNode? FindNextTargetableNodeAndUpdateIndex(CurrentRequest currentRequest)
    {
        for (int i = 0; i < currentRequest.Nodes.Count; ++i)
        {
            int currentIndex = (currentRequest.CurrentIndex + i) % currentRequest.Nodes.Count;
            GatheringNode currentNode = currentRequest.Nodes[currentIndex];
            List<IGameObject?> locationsAsObjects = currentNode.Locations.Select(x =>
                    objectTable.FirstOrDefault(y =>
                        currentNode.DataId == GameFunctions.GetBaseID(y) && Vector3.Distance(x.Position, y.Position) < 0.1f))
                //.OrderBy(x => _objectTable[0] is { } player && x != null ? Vector3.Distance(player.Position, x.Position) : 99999)
                .ToList();

            // Are any of the nodes too far away to be found? This is likely around ~100 yalms. All closer gathering
            // points are always in the object table, even if they're not targetable.
            if (locationsAsObjects.Any(x => x == null))
            {
                currentRequest.CurrentIndex = (currentIndex + 1) % currentRequest.Nodes.Count;
                return currentNode;
            }

            // If any are targetable, this group should be targeted as part of the route.
            if (locationsAsObjects.Any(x => x is { IsTargetable: true }))
            {
                currentRequest.CurrentIndex = (currentIndex + 1) % currentRequest.Nodes.Count;
                return currentNode;
            }
        }

        // unsure what to even do here
        return null;
    }

    public override IList<string> GetRemainingTaskNames()
    {
        if (_taskQueue.CurrentTaskExecutor?.CurrentTask is { } currentTask)
            return [currentTask.ToString() ?? "?", .. base.GetRemainingTaskNames()];

        return base.GetRemainingTaskNames();
    }

    public void OnNormalToast(SeString message)
    {
        if (_revisitRegex.IsMatch(message.TextValue))
        {
            if (_taskQueue.CurrentTaskExecutor?.CurrentTask is IRevisitAware currentTaskRevisitAware)
                currentTaskRevisitAware.OnRevisit();

            foreach (ITask task in _taskQueue.RemainingTasks)
            {
                if (task is IRevisitAware taskRevisitAware)
                    taskRevisitAware.OnRevisit();
            }
        }
    }

    internal sealed class CurrentRequest
    {
        public required GatheringRequest Data { get; init; }
        public required GatheringRoot Root { get; init; }

        /// <summary>
        ///     To make indexing easy with <see cref="CurrentIndex" />, we flatten the list of gathering locations.
        /// </summary>
        public required List<GatheringNode> Nodes { get; init; }

        public int CurrentIndex { get; set; }
    }

    public sealed record GatheringRequest
    (
        GatheringPointId GatheringPointId,
        uint ItemId,
        uint AlternativeItemId,
        int Quantity,
        ushort Collectability = 0);
}
