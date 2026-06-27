using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using static Questionable.Utils.LocalizeShortcut;

namespace Questionable.Windows.Utils;

internal sealed class ItemBlacklistSelector(IDataManager dataManager)
{
    private const int MaxResults = 50;
    private string _searchString = string.Empty;

    public Action<uint>? ItemSelected { private get; set; }

    public void DrawSelection(IReadOnlySet<uint> blacklist)
    {
        if (ItemSelected == null)
            throw new InvalidOperationException("ItemSelected action must be set before drawing the item selector.");

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (!ImGui.BeginCombo("##ItemBlacklistSelection", _L("Add item..."), ImGuiComboFlags.HeightLarge))
            return;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        bool addFirst = ImGui.InputTextWithHint("##filter", _L("Search by name or item ID..."), ref _searchString, 256,
            ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue);

        if (string.IsNullOrWhiteSpace(_searchString))
        {
            ImGui.TextDisabled(_L("Type an item name or ID to search."));
        }
        else
        {
            int shown = 0;
            if (uint.TryParse(_searchString, out uint itemId) &&
                !blacklist.Contains(itemId))
            {
                Item? itemById = dataManager.GetExcelSheet<Item>()?.GetRowOrDefault(itemId);
                if (itemById != null &&
                    (ImGui.Selectable($"{itemById.Value.Name} ({itemId})") || addFirst))
                {
                    ItemSelected(itemId);
                    if (addFirst)
                    {
                        ImGui.CloseCurrentPopup();
                        addFirst = false;
                    }
                }
            }

            foreach (Item item in dataManager.GetExcelSheet<Item>())
            {
                if (item.RowId == 0 || blacklist.Contains(item.RowId))
                    continue;

                string name = item.Name.ToString();
                if (!name.Contains(_searchString, StringComparison.CurrentCultureIgnoreCase) &&
                    !item.RowId.ToString(CultureInfo.InvariantCulture).Contains(_searchString, StringComparison.Ordinal))
                    continue;

                if (ImGui.Selectable($"{name} ({item.RowId})") || addFirst)
                {
                    ItemSelected(item.RowId);
                    if (addFirst)
                    {
                        ImGui.CloseCurrentPopup();
                        addFirst = false;
                    }
                }

                if (++shown >= MaxResults)
                {
                    ImGui.TextDisabled(_LF("Showing first {0} matches — refine your search.", MaxResults));
                    break;
                }
            }
        }

        ImGui.EndCombo();
        ImGui.Spacing();
    }
}
