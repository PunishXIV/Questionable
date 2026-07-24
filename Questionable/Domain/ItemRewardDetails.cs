using Dalamud.Utility;
using Lumina.Excel.Sheets;
using Questionable.Model.Questing;

namespace Questionable.Domain;

public sealed class ItemRewardDetails(Item item, ElementId elementId)
{
    public uint ItemId { get; } = item.RowId;
    public string Name { get; } = item.Name.ToDalamudString().ToString();
    public TimeSpan CastTime { get; } = TimeSpan.FromSeconds(item.CastTimeSeconds);
    public ElementId ElementId { get; } = elementId;
}
