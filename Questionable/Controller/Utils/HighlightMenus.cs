using Dalamud.Game.NativeWrapper;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using Lumina.Text.ReadOnly;
using TerritoryType = Lumina.Excel.Sheets.TerritoryType;

namespace Questionable.Controller.Utils;

// Adapted from https://github.com/electr0sheep/ItemVendorLocation/blob/main/ItemVendorLocation/HighlightMenus.cs
internal class HighlightMenus : IDisposable
{
    private readonly ICondition _condition;
    private readonly Configuration _configuration;
    private readonly IDataManager _dataManager;
    private readonly IFramework _framework;
    private readonly ILogger<HighlightMenus> _logger;
    private readonly IObjectTable _objectTable;
    private readonly IGameGui _gameGui;

    private NpcInfo[] _npcInfo = [];
    private ItemInfo? _itemInfo;

    public HighlightMenus(
        IFramework framework,
        Configuration configuration,
        ICondition condition,
        IObjectTable objectTable,
        IDataManager dataManager,
        IGameGui gameGui,
        ILogger<HighlightMenus> logger)
    {
        _framework = framework;
        _configuration = configuration;
        _condition = condition;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _logger = logger;
        _gameGui = gameGui;
        _framework.Update += Framework_OnUpdate;
    }

    private unsafe void Framework_OnUpdate(IFramework framework)
    {
        if (_npcInfo.Length == 0)
            return;

        HighlightShopAddon();
        HighlightSelectIconStringAddon();
        HighlightSelectStringAddon();
        HighlightInclusionShopAddon();
        HighlightShopExchangeCurrencyAddon();
        HighlightShopExchangeItemAddon();
        HighlightCollectablesShopAddon();
    }

    private unsafe void HighlightShopAddon()
    {
        if (_itemInfo == null)
            return;
        AtkUnitBasePtr shopAddonPtr = _gameGui.GetAddonByName("Shop");
        if (shopAddonPtr == nint.Zero)
            return;

        var shopAddon = (AtkUnitBase*)shopAddonPtr.Address;

        var itemList = (AtkComponentList*)shopAddon->GetComponentByNodeId(16);

        var bestMatchIndex = uint.MaxValue;

        foreach (uint index in Enumerable.Range(0, itemList->ListLength).Select(v => (uint)v))
        {
            AtkComponentListItemRenderer* listItemRenderer = itemList->ItemRendererList[index].AtkComponentListItemRenderer;

            if (listItemRenderer == null)
                continue;
            AtkTextNode* text = listItemRenderer->GetTextNodeById(3);
            if (text == null)
                continue;
            var itemName = ((ReadOnlySeStringSpan)text->NodeText.AsSpan()).ExtractText();
            // I use a partial matching because I guess item names can be concatenated. I don't think what I came up with
            // is foolproof, but it's good enough for now. I'm trying to figure out if I can use the agent for exact name
            // matches, but what I'm seeing doesn't quite match up with what I see in CS. So until I figure that out, I'm
            // going with this.
            if (string.Equals(_itemInfo.Name, itemName, StringComparison.Ordinal))
            {
                // if we ever find an exact match, that must be it, so highlight it and return.
                text->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
                text->SetText(itemName);
                return;
            }
            if (itemName.EndsWith("..."))
            {
                if (_itemInfo.Name is string infoName && infoName.StartsWith(itemName.TrimEnd('.')))
                    bestMatchIndex = index;
            }
        }

        if (bestMatchIndex != uint.MaxValue)
        {
            AtkComponentListItemRenderer* listItemRenderer = itemList->ItemRendererList[bestMatchIndex].AtkComponentListItemRenderer;
            AtkTextNode* text = listItemRenderer->GetTextNodeById(3);
            var itemName = ((ReadOnlySeStringSpan)text->NodeText.AsSpan()).ExtractText();
            text->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
            // strangely, it doesn't seem like the list gets its color updated until we set the text below
            text->SetText(itemName);
        }
    }

    private unsafe void HighlightSelectIconStringAddon()
    {
        AtkUnitBasePtr selectIconStringAddonPtr = _gameGui.GetAddonByName("SelectIconString");

        if (selectIconStringAddonPtr == nint.Zero)
            return;

        var selectIconStringAddon = (AtkUnitBase*)selectIconStringAddonPtr.Address;

        AtkComponentList* componentList = selectIconStringAddon->GetComponentListById(3);

        if (componentList == null)
            return;

        foreach (uint index in Enumerable.Range(0, componentList->ListLength).Select(v => (uint)v))
        {
            AtkComponentListItemRenderer* listItemRenderer = componentList->ItemRendererList[index].AtkComponentListItemRenderer;
            if (listItemRenderer == null)
                continue;
            AtkTextNode* text = listItemRenderer->GetTextNodeById(2);
            if (text == null)
                continue;
            try
            {
                if (_npcInfo.Any(n => n.ShopName != null && n.ShopName.Split("\n").Any(s => string.Equals(s, text->NodeText.ToString(), StringComparison.Ordinal))))
                {
                    text->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
                    return;
                }
            }
            catch (NullReferenceException)
            {
                continue;
            }
        }
    }

    private unsafe void HighlightSelectStringAddon()
    {
        AtkUnitBasePtr selectIconStringAddonPtr = _gameGui.GetAddonByName("SelectString");

        if (selectIconStringAddonPtr == nint.Zero)
            return;

        var selectIconStringAddon = (AtkUnitBase*)selectIconStringAddonPtr.Address;

        AtkComponentList* componentList = selectIconStringAddon->GetComponentListById(3);

        if (componentList == null)
            return;

        foreach (uint index in Enumerable.Range(0, componentList->ListLength).Select(v => (uint)v))
        {
            AtkComponentListItemRenderer* listItemRenderer = componentList->ItemRendererList[index].AtkComponentListItemRenderer;
            if (listItemRenderer == null)
                continue;
            AtkTextNode* text = listItemRenderer->GetTextNodeById(2);
            if (text == null)
                continue;
            try
            {
                if (_npcInfo.Any(n => n.ShopName != null && n.ShopName.Split("\n").Any(s => string.Equals(s, ((ReadOnlySeStringSpan)text->NodeText.AsSpan()).ExtractText(), StringComparison.Ordinal))))
                {
                    text->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
                    return;
                }
            }
            catch (NullReferenceException)
            {
                continue;
            }
        }
    }

    private unsafe void HighlightInclusionShopAddon()
    {
        AtkUnitBasePtr inclusionShopAddonPtr = _gameGui.GetAddonByName("InclusionShop");

        if (inclusionShopAddonPtr == nint.Zero)
            return;

        var inclusionShopAddon = (AtkUnitBase*)inclusionShopAddonPtr.Address;

        var category = (AtkComponentDropDownList*)inclusionShopAddon->GetComponentByNodeId(7);
        var subcategory = (AtkComponentDropDownList*)inclusionShopAddon->GetComponentByNodeId(9);
        var itemList = (AtkComponentTreeList*)inclusionShopAddon->GetComponentByNodeId(19);

        if (category == null || subcategory == null)
            return;

        foreach (uint index in Enumerable.Range(0, category->List->ListLength).Select(v => (uint)v))
        {
            AtkComponentListItemRenderer* listItemRenderer = category->List->ItemRendererList[index].AtkComponentListItemRenderer;
            if (listItemRenderer == null)
                continue;
            var text = (AtkTextNode*)listItemRenderer->GetTextNodeById(4);
            if (text == null)
                continue;
            var textValue = ((ReadOnlySeStringSpan)text->NodeText.AsSpan()).ExtractText();
            try
            {
                if (_npcInfo.Any(n => n.ShopName != null && n.ShopName.Split("\n").Any(s => string.Equals(s, textValue, StringComparison.Ordinal))))
                {
                    text->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
                    break;
                }
            }
            catch (NullReferenceException)
            {
                continue;
            }
        }
        foreach (uint index in Enumerable.Range(0, subcategory->List->ListLength).Select(v => (uint)v))
        {
            AtkComponentListItemRenderer* listItemRenderer = subcategory->List->ItemRendererList[index].AtkComponentListItemRenderer;
            if (listItemRenderer == null)
                continue;
            var text = (AtkTextNode*)listItemRenderer->GetTextNodeById(4);
            if (text == null)
                continue;
            var textValue = ((ReadOnlySeStringSpan)text->NodeText.AsSpan()).ExtractText();
            try
            {
                if (_npcInfo.Any(n => n.ShopName != null && n.ShopName.Split("\n").Any(s => string.Equals(s, textValue, StringComparison.Ordinal))))
                {
                    text->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
                    break;
                }
            }
            catch (NullReferenceException)
            {
                continue;
            }
        }

        if (itemList == null)
            return;

        foreach (Pointer<AtkComponentTreeListItem> item in itemList->Items)
        {
            AtkComponentListItemRenderer* listItemRenderer = item.Value->Renderer;
            if (listItemRenderer == null)
                continue;
            var text = (AtkTextNode*)listItemRenderer->GetTextNodeById(5);
            if (text == null)
                continue;
            var itemName = ((ReadOnlySeStringSpan)text->NodeText.AsSpan()).ExtractText();
            if (itemName == _itemInfo?.Name)
            {
                text->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
                // strangely, it doesn't seem like the list gets its color updated until we set the text below
                text->SetText(itemName);
                return;
            }
        }
    }

    private unsafe void HighlightShopExchangeCurrencyAddon()
    {
        AtkUnitBasePtr shopExchangeCurrencyAddonPtr = _gameGui.GetAddonByName("ShopExchangeCurrency");

        if (shopExchangeCurrencyAddonPtr == nint.Zero)
            return;

        var shopExchangeCurrencyAddon = (AtkUnitBase*)shopExchangeCurrencyAddonPtr.Address;

        // highlight tab
        var tabs = (AtkResNode*)shopExchangeCurrencyAddon->GetNodeById(7);

        if (tabs != null)
        {
            AtkResNode* othersTab = tabs->ChildNode;
            AtkResNode* accessoriesTab = othersTab->PrevSiblingNode;
            AtkResNode* armorTab = accessoriesTab->PrevSiblingNode;
            AtkResNode* weaponsTab = armorTab->PrevSiblingNode;
            if (othersTab != null && _itemInfo?.SpecialShopCategory == 4)
            {
                othersTab->GetAsAtkComponentRadioButton()->GetTextNodeById(2)->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
            }
            if (accessoriesTab != null && _itemInfo?.SpecialShopCategory == 3)
            {
                accessoriesTab->GetAsAtkComponentRadioButton()->GetTextNodeById(2)->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
            }
            if (armorTab != null && _itemInfo?.SpecialShopCategory == 2)
            {
                armorTab->GetAsAtkComponentRadioButton()->GetTextNodeById(2)->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
            }
            if (weaponsTab != null && _itemInfo?.SpecialShopCategory == 1)
            {
                weaponsTab->GetAsAtkComponentRadioButton()->GetTextNodeById(2)->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
            }
        }

        // highlight item in list
        var itemList = (AtkComponentTreeList*)shopExchangeCurrencyAddon->GetComponentByNodeId(19);

        if (itemList == null)
            itemList = (AtkComponentTreeList*)shopExchangeCurrencyAddon->GetComponentByNodeId(20);

        if (itemList == null)
            return;

        foreach (Pointer<AtkComponentTreeListItem> item in itemList->Items)
        {
            AtkComponentListItemRenderer* listItemRenderer = item.Value->Renderer;
            if (listItemRenderer == null)
                continue;
            var text = (AtkTextNode*)listItemRenderer->GetTextNodeById(3);
            if (text == null)
                text = (AtkTextNode*)listItemRenderer->GetTextNodeById(8);
            if (text == null)
                continue;
            var itemName = ((ReadOnlySeStringSpan)text->NodeText.AsSpan()).ExtractText();
            if (itemName == _itemInfo?.Name)
            {
                text->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
                // strangely, it doesn't seem like the list gets its color updated until we set the text below
                text->SetText(itemName);
                return;
            }
        }
    }

    private unsafe void HighlightShopExchangeItemAddon()
    {
        AtkUnitBasePtr shopExchangeItemAddonPtr = _gameGui.GetAddonByName("ShopExchangeItem");

        if (shopExchangeItemAddonPtr == nint.Zero)
            return;

        var shopExchangeItemAddon = (AtkUnitBase*)shopExchangeItemAddonPtr.Address;

        var itemList = (AtkComponentTreeList*)shopExchangeItemAddon->GetComponentByNodeId(20);

        if (itemList == null)
            return;

        foreach (Pointer<AtkComponentTreeListItem> item in itemList->Items)
        {
            AtkComponentListItemRenderer* listItemRenderer = item.Value->Renderer;
            if (listItemRenderer == null)
                continue;
            var text = (AtkTextNode*)listItemRenderer->GetTextNodeById(7);
            if (text == null)
                continue;
            var itemName = ((ReadOnlySeStringSpan)text->NodeText.AsSpan()).ExtractText();
            if (itemName == _itemInfo?.Name)
            {
                text->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
                // strangely, it doesn't seem like the list gets its color updated until we set the text below
                text->SetText(itemName);
                return;
            }
        }
    }

    private unsafe void HighlightCollectablesShopAddon()
    {
        AtkUnitBasePtr collectablesShopAddonPtr = _gameGui.GetAddonByName("CollectablesShop");

        if (collectablesShopAddonPtr == nint.Zero)
            return;

        var collectablesShopAddon = (AtkUnitBase*)collectablesShopAddonPtr.Address;

        try
        {
            NpcInfo shop = _npcInfo.First(n => n.ShopName is string _shopName && _shopName.Contains("Oddly Specific Materials Exchange"));
            if (shop.ShopName == null || shop.Costs == null)
                return;
            var shopType = shop.ShopName.Split("\n")[1].Split("Oddly Specific Materials Exchange (")[1].Split(")")[0];
            CollectablesShopIconIndex index = Enum.GetValues<CollectablesShopIconIndex>()[Enum.GetNames<CollectablesShopIconIndex>().ToList().FindIndex(e => e == shopType)];
            var itemCost = shop.Costs[0].Item2.Split(" min ")[0];

            var radioButton = (AtkComponentRadioButton*)collectablesShopAddon->GetComponentByNodeId((uint)index);
            radioButton->ButtonBGNode->Color = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);

            var itemList = (AtkComponentTreeList*)collectablesShopAddon->GetComponentByNodeId(28);

            if (itemList == null)
                return;

            foreach (Pointer<AtkComponentTreeListItem> item in itemList->Items)
            {
                AtkComponentListItemRenderer* listItemRenderer = item.Value->Renderer;
                if (listItemRenderer == null)
                    continue;
                var text = (AtkTextNode*)listItemRenderer->GetTextNodeById(4);
                if (text == null)
                    continue;
                var itemName = ((ReadOnlySeStringSpan)text->NodeText.AsSpan()).ExtractText().Split(" ")[0];
                if (itemName == itemCost)
                {
                    text->TextColor = Dalamud.Utility.Numerics.VectorExtensions.ToByteColor(ImGuiColors.DalamudRed);
                    // strangely, it doesn't seem like the list gets its color updated until we set the text below
                    text->SetText(((ReadOnlySeStringSpan)text->NodeText.AsSpan()).ExtractText());
                    return;
                }
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }
    }

    public void SetNpcInfo(NpcInfo[] npcInfos)
    {
        _npcInfo = npcInfos;
    }

    public void AddNpcInfo(NpcInfo npcInfo)
    {
        _npcInfo = _npcInfo.Append(npcInfo).ToArray();
    }

    public void SetItemInfo(ItemInfo item)
    {
        _itemInfo = item;
    }

    public void ClearAllInfo()
    {
        _npcInfo = [];
        _itemInfo = null;
    }

    public void Dispose()
    {
        _framework.Update -= Framework_OnUpdate;
    }

    public class NpcInfo
    {
        public uint Id;
        public string? Name;
        public string? ShopName;
        public List<Tuple<uint, string>>? Costs;
        public NpcLocation? Location;
    }

    public class NpcLocation(float x, float y, TerritoryType territoryType, uint? map = null)
    {
        public float MapX => ToMapCoordinate(X, territoryType.Map.Value.SizeFactor, territoryType.Map.Value.OffsetX);
        public float MapY => ToMapCoordinate(Y, territoryType.Map.Value.SizeFactor, territoryType.Map.Value.OffsetY);
        public float X { get; } = x;
        public float Y { get; } = y;
        public uint TerritoryType => territoryType.RowId;
        public uint MapId { get; } = map != null ? (uint)map : territoryType.Map.RowId;

        private static float ToMapCoordinate(float val, float scale, short offset)
        {
            var c = scale / 100.0f;

            val = (val + offset) * c;

            return (41.0f / c * ((val + 1024.0f) / 2048.0f)) + 1;
        }
    }

    public enum ItemType
    {
        GilShop,
        SpecialShop,
        GcShop,
        Achievement,
        FcShop,
        QuestReward,
        CollectableExchange,
    }

    public class ItemInfo
    {
        public uint Id;
        public string? Name;
        public List<NpcInfo>? NpcInfos;
        public ItemType Type;
        public string? AchievementDescription;
        public uint SpecialShopCategory;

        public bool HasShopNames()
        {
            return NpcInfos!.Any(i => i.ShopName != null);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1069:Enums values should not be duplicated", Justification = "<Pending>")]
    public enum CollectablesShopIconIndex : uint
    {
        Carpenter, Carpentry = 3,
        Blacksmith, Blacksmithing = 4,
        Armoer, Armoring = 5,
        Goldsmith, Goldsmithing = 6,
        Leatherworker, Leatherworking = 7,
        Weaver, Clothcrafting = 8,
        Alchemist, Alchemy = 9,
        Culinarian, Cooking = 10,
        Miner = 11,
        Botanist = 12,
        Fisher = 13,
    }
}
