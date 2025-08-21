using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Microsoft.Extensions.Logging;

namespace Questionable.External;

internal sealed class ArtisanIpc
{
    private readonly ILogger<ArtisanIpc> _logger;
    private readonly ICallGateSubscriber<ushort, int, object> _craftItem;
    private readonly ICallGateSubscriber<bool> _getEnduranceStatus;

    public ArtisanIpc(IDalamudPluginInterface pluginInterface, ILogger<ArtisanIpc> logger)
    {
        _logger = logger;
        _craftItem = pluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
        _getEnduranceStatus = pluginInterface.GetIpcSubscriber<bool>("Artisan.GetEnduranceStatus");
    }

    public bool CraftItem(ushort recipeId, int quantity)
    {
        try
        {
            _logger.LogInformation("Attempting to craft {Quantity} items with recipe {RecipeId} with Artisan", quantity,
                recipeId);
            _craftItem.InvokeAction(recipeId, quantity);
            return true;
        }
        catch (IpcError e)
        {
            _logger.LogError(e, "Unable to craft items");
            return false;
        }
    }

    /// <summary>
    /// This ignores crafting lists, but we can't create/use those.
    /// </summary>
    public bool IsCrafting()
    {
        try
        {
            return _getEnduranceStatus.InvokeFunc();
        }
        catch (IpcError e)
        {
            _logger.LogError(e, "Unable to check for Artisan endurance status");
            return false;
        }
    }
}
