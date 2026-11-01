using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton<IArtisanIpc, ArtisanIpc>]
internal sealed class ArtisanIpc(IDalamudPluginInterface pluginInterface, ILogger<ArtisanIpc> logger,
    CraftingOperationState craftingOperation) : Ipc, IArtisanIpc
{
    public override string InternalName => "Artisan";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => !craftingOperation.HasOperation && GetStatus() == ArtisanStatus.Idle;

    private readonly ICallGateSubscriber<ushort, int, object> _craftItem = pluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
    private readonly ICallGateSubscriber<Dictionary<int, string>> _getLists = pluginInterface.GetIpcSubscriber<Dictionary<int, string>>("Artisan.GetLists");
    private readonly ICallGateSubscriber<bool> _getStopRequest = pluginInterface.GetIpcSubscriber<bool>("Artisan.GetStopRequest");
    private readonly ICallGateSubscriber<bool> _isListPaused = pluginInterface.GetIpcSubscriber<bool>("Artisan.IsListPaused");
    private readonly ICallGateSubscriber<bool> _isListRunning = pluginInterface.GetIpcSubscriber<bool>("Artisan.IsListRunning");
    private readonly ICallGateSubscriber<bool> _getEnduranceStatus = pluginInterface.GetIpcSubscriber<bool>("Artisan.GetEnduranceStatus");
    private readonly ICallGateSubscriber<int, object> _startListById = pluginInterface.GetIpcSubscriber<int, object>("Artisan.StartListById");
    private readonly ICallGateSubscriber<bool> _isBusy = pluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");

    public ArtisanStartResult CraftItem(ushort recipeId, int crafts)
    {
        if (recipeId == 0 || crafts <= 0)
            return ArtisanStartResult.Failed;

        ArtisanStartResult? rejection = CheckReady();
        if (rejection.HasValue)
            return rejection.Value;

        Guid? operationId = null;
        try
        {
            logger.LogInformation("Submitting {Crafts} crafts with recipe {RecipeId} to Artisan", crafts,
                recipeId);
            operationId = craftingOperation.BeginSubmission();
            logger.LogDebug("Tracking Artisan recipe submission {OperationId}", operationId);
            _craftItem.InvokeAction(recipeId, crafts);
            return ArtisanStartResult.Submitted;
        }
        catch (IpcError e)
        {
            if (operationId.HasValue)
                craftingOperation.MarkUncertain(operationId.Value);
            logger.LogError(e, "Unable to craft items");
            return ArtisanStartResult.Unavailable;
        }
        catch (Exception e)
        {
            if (operationId.HasValue)
                craftingOperation.MarkUncertain(operationId.Value);
            logger.LogError(e, "Artisan recipe submission failed; it may have queued work, do not retry automatically");
            return ArtisanStartResult.Failed;
        }
    }

    public ArtisanStartResult CraftList(int listId)
    {
        ArtisanStartResult? rejection = CheckReady();
        if (rejection.HasValue)
            return rejection.Value;

        Guid? operationId = null;
        try
        {
            // Only a successful catalogue query can establish that fallback is appropriate.
            if (!_getLists.InvokeFunc().ContainsKey(listId))
                return ArtisanStartResult.MissingList;

            rejection = CheckReady();
            if (rejection.HasValue)
                return rejection.Value;

            logger.LogInformation("Submitting list {ListId} to Artisan", listId);
            operationId = craftingOperation.BeginSubmission();
            logger.LogDebug("Tracking Artisan list submission {OperationId}", operationId);
            _startListById.InvokeAction(listId);
            return ArtisanStartResult.Submitted;
        }
        catch (IpcError e)
        {
            if (operationId.HasValue)
                craftingOperation.MarkUncertain(operationId.Value);
            logger.LogError(e, "Unable to submit Artisan list");
            return ArtisanStartResult.Unavailable;
        }
        catch (Exception e)
        {
            if (operationId.HasValue)
                craftingOperation.MarkUncertain(operationId.Value);
            logger.LogError(e, "Artisan list submission failed; it may have queued work, do not fall back");
            return ArtisanStartResult.Failed;
        }
    }

    private ArtisanStartResult? CheckReady() => craftingOperation.HasOperation ? ArtisanStartResult.Busy : GetStatus() switch
    {
        ArtisanStatus.Idle => null,
        ArtisanStatus.Unavailable => ArtisanStartResult.Unavailable,
        _ => ArtisanStartResult.Busy,
    };

    public ArtisanStatus GetStatus()
    {
        try
        {
            // Never clear another caller's stop request or resume its paused list.
            if (_getStopRequest.InvokeFunc() || _isListPaused.InvokeFunc())
                return ArtisanStatus.Paused;
            if (_isListRunning.InvokeFunc() || _getEnduranceStatus.InvokeFunc())
                return ArtisanStatus.Busy;
            return _isBusy.InvokeFunc() ? ArtisanStatus.Queued : ArtisanStatus.Idle;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Unable to check Artisan status");
            return ArtisanStatus.Unavailable;
        }
    }
}
