#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
using Lumina.Excel.Sheets;

namespace Questionable.External;

internal class ItemVendorLocationIpc
{
    private readonly HighlightObject _highlightObject;
    private readonly HighlightMenus _highlightMenus;
    private readonly IDataManager _dataManager;
    private readonly IClientState _clientState;
    private readonly ILogger<ItemVendorLocationIpc> _logger;
    private delegate HashSet<(uint npcId, uint territory, (float x, float y))>? GetItemVendorsDelegate(
       uint itemId,
       bool filterNoLocation);
    [EzIPC("GetItemVendors")] private readonly GetItemVendorsDelegate _getItemInfoProvider;

    public ItemVendorLocationIpc(
        HighlightObject highlightObject,
        HighlightMenus highlightMenus,
        IDataManager dataManager,
        IClientState clientState,
        ILogger<ItemVendorLocationIpc> logger)
    {
        _highlightObject = highlightObject;
        _highlightMenus = highlightMenus;
        _dataManager = dataManager;
        _clientState = clientState;
        _logger = logger;
        EzIPC.Init(this, "ItemVendorLocation", SafeWrapper.IPCException);
    }

    public bool HighlightItemVendors(uint itemId)
    {
        _logger.LogInformation("Querying Item Vendor Location for {Item}", itemId);
        var npcInfo = IpcInvoke.SafeFunc(() =>
        {
            return _getItemInfoProvider(itemId, filterNoLocation: true);
        }, fallback: default, _logger, "Unable to call Item Vendor Location");
        if (npcInfo == null)
            return false;

        foreach (var npc in npcInfo)
        {
            (uint npcId, uint territory, (float x, float y)) = npc;
            if (!territory.Equals(_clientState.TerritoryType))
                continue;
            _highlightObject.AddHighlight(npcId);
            var territoryExcel = _dataManager.GetExcelSheet<TerritoryType>().GetRow(territory);
            _highlightMenus.AddNpcInfo(new()
            {
                Id = npcId,
                Location = new(x, y, territoryExcel)
            });
        }
        return true;
    }
}
