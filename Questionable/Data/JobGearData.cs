using ECommons.ExcelServices;
using Lumina.Excel.Sheets;
namespace Questionable.Data;

[RegisterSingleton]
internal sealed class JobGearData(IDataManager dataManager)
{
    internal sealed record Vendor(int Level, uint NpcId, uint TerritoryId, Vector3 Position, uint[] ShopIds);

    internal sealed record GearSet(Vendor Vendor, string ShopName, QuestId UnlockQuest, int ItemLevel,
        IReadOnlyList<uint> Items);

    internal static readonly Vendor[] Vendors =
    [
        new(99, 1048376, 1185, new(-48.949f, -10f, 93.496f), [1770757, 1770758, 1770759, 1770760]), // Mewazunte
        new(89, 1037047, 962, new(26.748f, 5.15f, -65.851f), [1770450, 1770451, 1770452, 1770453]), // Varsarudh
        new(80, 1030520, 818, new(638.989f, 402.212f, 212.794f), [1769965, 1769966, 1769967, 1769968]), // Grenoldt
    ];

    private readonly Dictionary<(uint NpcId, Job Job), GearSet?> _sets = [];

    // Each vendor has one shop per role; the job's pieces are found by the items' job category.
    public GearSet? GetSet(Vendor vendor, Job job)
    {
        if (_sets.TryGetValue((vendor.NpcId, job), out GearSet? known))
            return known;

        GearSet? set = null;
        ExcelSheet<SpecialShop> shops = dataManager.GetExcelSheet<SpecialShop>();
        foreach (uint shopId in vendor.ShopIds)
        {
            if (shops.GetRowOrDefault(shopId) is not { } shop || shop.Quest.RowId <= 0x10000)
                continue;

            List<Item> items = shop.Item
                .SelectMany(x => x.ReceiveItems)
                .Select(x => x.Item.ValueNullable)
                .OfType<Item>()
                .Where(x => x.RowId != 0 && x.ClassJobCategory.ValueNullable is { } category &&
                            QuestInfoUtils.AsList(category).Contains(job))
                .DistinctBy(x => x.RowId)
                .ToList();
            if (items.Count == 0)
                continue;

            set = new GearSet(vendor, shop.Name.ExtractText(), new QuestId((ushort)(shop.Quest.RowId - 0x10000)),
                (int)items.Max(x => x.LevelItem.RowId), items.Select(x => x.RowId).ToList());
            break;
        }

        _sets[(vendor.NpcId, job)] = set;
        return set;
    }
}
