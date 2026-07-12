using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;
namespace Questionable.Gear;

public sealed class GearStatsCalculator
{
    private const uint EternityRingItemId = 8575;
    private static readonly uint[] CanHaveOffhand = [2, 6, 8, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32];
    private readonly Dictionary<(EBaseParam BaseParam, int EquipSlotCategory), ushort> _equipSlotCategoryPct;
    private readonly Dictionary<(uint ItemLevel, EBaseParam BaseParam), ushort> _itemLevelStatCaps = [];

    private readonly ExcelSheet<Item> _itemSheet;
    private readonly Dictionary<uint, MateriaInfo> _materiaStats;

    public GearStatsCalculator(IDataManager? dataManager)
        : this(dataManager?.GetExcelSheet<ItemLevel>() ?? throw new ArgumentNullException(nameof(dataManager)),
            dataManager.GetExcelSheet<ExtendedBaseParam>(),
            dataManager.GetExcelSheet<Materia>(),
            dataManager.GetExcelSheet<Item>())
    {
    }

    public GearStatsCalculator(ExcelSheet<ItemLevel> itemLevelSheet,
        ExcelSheet<ExtendedBaseParam> baseParamSheet,
        ExcelSheet<Materia> materiaSheet,
        ExcelSheet<Item> itemSheet)
    {
        ArgumentNullException.ThrowIfNull(itemLevelSheet);
        ArgumentNullException.ThrowIfNull(baseParamSheet);
        ArgumentNullException.ThrowIfNull(materiaSheet);
        ArgumentNullException.ThrowIfNull(itemSheet);

        _itemSheet = itemSheet;

        foreach (ItemLevel itemLevel in itemLevelSheet)
        {
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.Strength)] = itemLevel.Strength;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.Dexterity)] = itemLevel.Dexterity;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.Vitality)] = itemLevel.Vitality;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.Intelligence)] = itemLevel.Intelligence;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.Mind)] = itemLevel.Mind;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.Piety)] = itemLevel.Piety;

            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.GP)] = itemLevel.GP;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.CP)] = itemLevel.CP;

            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.DamagePhys)] = itemLevel.PhysicalDamage;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.DamageMag)] = itemLevel.MagicalDamage;

            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.DefensePhys)] = itemLevel.Defense;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.DefenseMag)] = itemLevel.MagicDefense;

            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.Tenacity)] = itemLevel.Tenacity;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.Crit)] = itemLevel.CriticalHit;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.DirectHit)] = itemLevel.DirectHitRate;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.Determination)] = itemLevel.Determination;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.SpellSpeed)] = itemLevel.SpellSpeed;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.SkillSpeed)] = itemLevel.SkillSpeed;

            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.Gathering)] = itemLevel.Gathering;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.Perception)] = itemLevel.Perception;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.Craftsmanship)] = itemLevel.Craftsmanship;
            _itemLevelStatCaps[(itemLevel.RowId, EBaseParam.Control)] = itemLevel.Control;
        }

        _equipSlotCategoryPct = baseParamSheet
            .SelectMany(x => Enumerable.Range(0, x.EquipSlotCategoryPct.Count)
                .Select(y => ((EBaseParam)x.RowId, y, x.EquipSlotCategoryPct[y])))
            .ToDictionary(x => (x.Item1, x.y), x => x.Item3);

        _materiaStats = materiaSheet.Where(x => x.RowId > 0 && x.BaseParam.RowId > 0)
            .ToDictionary(x => x.RowId,
                x => new MateriaInfo((EBaseParam)x.BaseParam.RowId, x.Value, x.Item[0].RowId > 0));
    }

    public unsafe EquipmentStats CalculateGearStats(InventoryItem* item)
    {
        List<(uint, byte)> materias = [];
        byte materiaCount = 0;
        if (item->ItemId != EternityRingItemId)
        {
            for (int i = 0; i < 5; ++i)
            {
                ushort materia = item->Materia[i];
                if (materia != 0)
                {
                    materiaCount++;
                    materias.Add((materia, item->MateriaGrades[i]));
                }
            }
        }

        return CalculateGearStats(_itemSheet.GetRow(item->ItemId), item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality), materias) with
        {
            MateriaCount = materiaCount
        };
    }

    public EquipmentStats CalculateGearStats(Item item, bool highQuality,
        IReadOnlyList<(uint MateriaId, byte Grade)> materias)
    {
        ArgumentNullException.ThrowIfNull(materias);

        Dictionary<EBaseParam, StatInfo> result = [];
        for (int i = 0; i < item.BaseParam.Count; ++i)
        {
            AddEquipmentStat(result, item.BaseParam[i], item.BaseParamValue[i]);
        }

        if (highQuality)
        {
            for (int i = 0; i < item.BaseParamSpecial.Count; ++i)
            {
                AddEquipmentStat(result, item.BaseParamSpecial[i], item.BaseParamValueSpecial[i]);
            }
        }

        foreach ((uint MateriaId, byte Grade) in materias)
        {
            if (_materiaStats.TryGetValue(MateriaId, out MateriaInfo? materiaStat))
                AddMateriaStat(item, result, materiaStat, Grade);
        }

        return new(result, 0);
    }

    private static void AddEquipmentStat(Dictionary<EBaseParam, StatInfo> result, RowRef<BaseParam> baseParam,
        short value)
    {
        if (baseParam.RowId == 0)
            return;

        if (result.TryGetValue((EBaseParam)baseParam.RowId, out StatInfo? statInfo))
        {
            result[(EBaseParam)baseParam.RowId] =
                statInfo with { EquipmentValue = (short)(statInfo.EquipmentValue + value) };
        }
        else
        {
            result[(EBaseParam)baseParam.RowId] =
                new(value, 0, Overcapped: false);
        }
    }

    private void AddMateriaStat(Item item, Dictionary<EBaseParam, StatInfo> result, MateriaInfo materiaInfo,
        short grade)
    {
        if (!result.TryGetValue(materiaInfo.BaseParam, out StatInfo? statInfo))
            result[materiaInfo.BaseParam] = statInfo = new(0, 0, Overcapped: false);

        if (materiaInfo.HasItem)
        {
            short maximumValue = (short)(GetMaximumStatValue(item, materiaInfo.BaseParam) - statInfo.EquipmentValue);
            if (statInfo.MateriaValue + materiaInfo.Values[grade] > maximumValue)
            {
                result[materiaInfo.BaseParam] = statInfo with
                {
                    MateriaValue = maximumValue,
                    Overcapped = true
                };
            }
            else
            {
                result[materiaInfo.BaseParam] = statInfo with
                {
                    MateriaValue = (short)(statInfo.MateriaValue + materiaInfo.Values[grade])
                };
            }
        }
        else
        {
            result[materiaInfo.BaseParam] = statInfo with
            {
                MateriaValue = (short)(statInfo.MateriaValue + materiaInfo.Values[grade])
            };
        }
    }

    public short GetMaximumStatValue(Item item, EBaseParam baseParamValue)
    {
        if (_itemLevelStatCaps.TryGetValue((item.LevelItem.RowId, baseParamValue), out ushort stat))
        {
            return (short)Math.Round(
                stat * _equipSlotCategoryPct[(baseParamValue, (int)item.EquipSlotCategory.RowId)] / 1000f,
                MidpointRounding.AwayFromZero);
        }

        return 0;
    }

    public unsafe short CalculateAverageItemLevel(InventoryContainer* container)
    {
        uint sum = 0U;
        int calculatedSlots = 12;
        for (int i = 0; i < 13; i++)
        {
            if (i == 5)
                continue;

            InventoryItem* inventoryItem = container->GetInventorySlot(i);
            if (inventoryItem == null || inventoryItem->ItemId == 0)
                continue;

            Item? item = _itemSheet.GetRowOrDefault(inventoryItem->ItemId);
            if (item == null)
                continue;

            if (item.Value.ItemUICategory.RowId == 105)
            {
                if (i == 0)
                    calculatedSlots -= 1;
                calculatedSlots -= 1;
                continue;
            }

            if (i == 0 && !CanHaveOffhand.Contains(item.Value.ItemUICategory.RowId))
            {
                sum += item.Value.LevelItem.RowId;
                i++;
            }

            sum += item.Value.LevelItem.RowId;
        }

        return (short)(sum / calculatedSlots);
    }

    private sealed record MateriaInfo(EBaseParam BaseParam, Collection<short> Values, bool HasItem);
}

[Sheet("BaseParam")]
[SuppressMessage("Performance", "CA1815", Justification = "Lumina doesn't implement any equality ops")]
public unsafe readonly struct ExtendedBaseParam(ExcelPage page, uint offset, uint row)
    : IExcelRow<ExtendedBaseParam>
{
    private const int ParamCount = 23;

    public uint RowId => row;
    public BaseParam BaseParam => new(page, offset, row);

    public Collection<ushort> EquipSlotCategoryPct =>
        new(page, offset, offset, &EquipSlotCategoryPctCtor, ParamCount);

    ExcelPage IExcelRow<ExtendedBaseParam>.ExcelPage => page;

    uint IExcelRow<ExtendedBaseParam>.RowOffset => offset;

    private static ushort EquipSlotCategoryPctCtor(ExcelPage page, uint parentOffset, uint offset, uint i) => i == 0 ? (ushort)0 : page.ReadUInt16(offset + 8 + (i - 1) * 2);

    public static ExtendedBaseParam Create(ExcelPage page, uint offset, uint row) => new(page, offset, row);
}
