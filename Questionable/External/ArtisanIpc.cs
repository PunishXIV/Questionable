using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using ECommons;
using Microsoft.Extensions.Logging;
using Questionable.Model.Questing;
namespace Questionable.External;

internal sealed class ArtisanIpc(IDalamudPluginInterface pluginInterface, ILogger<ArtisanIpc> logger)
{
    private readonly ICallGateSubscriber<ushort, int, object> _craftItem = pluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
    private readonly ICallGateSubscriber<bool> _getEnduranceStatus = pluginInterface.GetIpcSubscriber<bool>("Artisan.GetEnduranceStatus");
    private readonly ICallGateSubscriber<bool> _isListRunning = pluginInterface.GetIpcSubscriber<bool>("Artisan.IsListRunning");
    private readonly ICallGateSubscriber<int, object> _startListById = pluginInterface.GetIpcSubscriber<int, object>("Artisan.StartListById");

    public bool CraftItem(ushort recipeId, int quantity)
    {
        try
        {
            logger.LogInformation("Attempting to craft {Quantity} items with recipe {RecipeId} with Artisan", quantity,
                recipeId);
            _craftItem.InvokeAction(recipeId, quantity);
            return true;
        }
        catch (IpcError e)
        {
            logger.LogError(e, "Unable to craft items");
            return false;
        }
    }

    public bool CraftList(int listId)
    {
        try
        {
            logger.LogInformation("Attempting to craft list {ListId} with Artisan", listId);
            _startListById.InvokeAction(listId);
            return true;
        }
        catch (IpcError e)
        {
            logger.LogError(e, "Unable to craft items");
            return false;
        }
        catch (Exception e)
        {
            logger.LogInformation(e, "CraftList failed");
            return false;
        }
    }

    public bool CraftList(ElementId questId) => CraftList(questId.Value.ToInt() + 65536);

    public bool IsCrafting()
    {
        try
        {
            return _getEnduranceStatus.InvokeFunc() || _isListRunning.InvokeFunc();
        }
        catch (IpcError e)
        {
            logger.LogError(e, "Unable to check for Artisanstatus");
            return false;
        }
    }
}
