using System.Collections.Immutable;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
namespace Questionable.Data;

internal sealed class AetherCurrentData(IDataManager dataManager)
{
    private readonly ImmutableDictionary<uint, ImmutableList<uint>> _overworldCurrents = dataManager.GetExcelSheet<AetherCurrentCompFlgSet>()
            .Where(x => x.RowId > 0 && x.Territory.IsValid)
            .ToImmutableDictionary(
                x => x.Territory.RowId,
                x => x.AetherCurrents
                    .Where(y => y.RowId > 0 && y.Value.Quest.RowId == 0)
                    .Select(y => y.RowId)
                    .ToImmutableList()
            );

    public bool IsValidAetherCurrent(uint territoryId, uint aetherCurrentId)
    {
        return _overworldCurrents.TryGetValue(territoryId, out ImmutableList<uint>? currentIds) &&
               currentIds.Contains(aetherCurrentId);
    }
}
