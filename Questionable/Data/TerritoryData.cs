using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using Questionable.Model.Questing;

namespace Questionable.Data;

internal sealed class TerritoryData
{
    private readonly ImmutableDictionary<uint, string> _territoryNames;
    private readonly ImmutableHashSet<ushort> _territoriesWithMount;
    private readonly ImmutableDictionary<ushort, uint> _dutyTerritories;
    private readonly ImmutableDictionary<uint, string> _instanceNames;
    private readonly ImmutableDictionary<uint, ContentFinderConditionData> _contentFinderConditions;
    private readonly ImmutableDictionary<(ElementId QuestId, byte Index), uint> _questBattlesToContentFinderCondition;

    public TerritoryData(IDataManager dataManager)
    {
        _territoryNames = dataManager.GetExcelSheet<TerritoryType>()
            .Where(x => x.RowId > 0)
            .Select(x =>
                new
                {
                    x.RowId,
                    Name = x.PlaceName.ValueNullable?.Name.ToString() ?? x.PlaceNameZone.ValueNullable?.Name.ToString(),
                })
            .Where(x => !string.IsNullOrEmpty(x.Name))
            .ToImmutableDictionary(x => x.RowId, x => x.Name!);

        _territoriesWithMount = dataManager.GetExcelSheet<TerritoryType>()
            .Where(x => x.RowId > 0 && x.Mount)
            .Select(x => (ushort)x.RowId)
            .ToImmutableHashSet();

        _dutyTerritories = dataManager.GetExcelSheet<TerritoryType>()
            .Where(x => x.RowId > 0 && x.ContentFinderCondition.RowId != 0)
            .ToImmutableDictionary(x => (ushort)x.RowId, x => x.ContentFinderCondition.Value.ContentType.RowId);

        _instanceNames = dataManager.GetExcelSheet<ContentFinderCondition>()
            .Where(x => x.RowId > 0 && x.Content.RowId != 0 && x.ContentLinkType == 1 && x.ContentType.RowId != 6)
            .ToImmutableDictionary(x => x.Content.RowId, x => x.Name.ToDalamudString().ToString());

        _contentFinderConditions = dataManager.GetExcelSheet<ContentFinderCondition>()
            .Where(x => x.RowId > 0 && x.Content.RowId != 0 && x.ContentLinkType is 1 or 5 && x.ContentType.RowId != 6)
            .Select(x => new ContentFinderConditionData(x, dataManager.Language))
            .ToImmutableDictionary(x => x.ContentFinderConditionId, x => x);

        _questBattlesToContentFinderCondition = dataManager.GetExcelSheet<Quest>()
            .Where(x => x is { RowId: > 0, IssuerLocation.RowId: > 0 })
            .SelectMany(GetQuestBattles)
            .Select(x => (x.QuestId, x.Index,
                CfcId: LookupContentFinderConditionForQuestBattle(dataManager, x.QuestBattleId)))
            .ToImmutableDictionary(x => (x.QuestId, x.Index), x => x.CfcId);
    }

    public string? GetName(ushort territoryId) => _territoryNames.GetValueOrDefault(territoryId);

    public string GetNameAndId(ushort territoryId)
    {
        string? territoryName = GetName(territoryId);
        if (territoryName != null)
            return string.Create(CultureInfo.InvariantCulture, $"{territoryName} ({territoryId})");
        else
            return territoryId.ToString(CultureInfo.InvariantCulture);
    }

    public bool CanUseMount(ushort territoryId) => _territoriesWithMount.Contains(territoryId);

    public bool IsDutyInstance(ushort territoryId) => _dutyTerritories.ContainsKey(territoryId);

    public bool IsQuestBattleInstance(ushort territoryId) =>
        _dutyTerritories.TryGetValue(territoryId, out uint contentType) && contentType == 7;

    public string? GetInstanceName(ushort instanceId) => _instanceNames.GetValueOrDefault(instanceId);

    public ContentFinderConditionData? GetContentFinderCondition(uint cfcId) =>
        _contentFinderConditions.GetValueOrDefault(cfcId);

    public bool TryGetContentFinderCondition(uint cfcId,
        [NotNullWhen(true)] out ContentFinderConditionData? contentFinderConditionData) =>
        _contentFinderConditions.TryGetValue(cfcId, out contentFinderConditionData);

    public bool TryGetContentFinderConditionForSoloInstance(ElementId questId, byte index,
        [NotNullWhen(true)] out ContentFinderConditionData? contentFinderConditionData)
    {
        if (_questBattlesToContentFinderCondition.TryGetValue((questId, index), out uint cfcId))
            return _contentFinderConditions.TryGetValue(cfcId, out contentFinderConditionData);
        else
        {
            contentFinderConditionData = null;
            return false;
        }
    }

    public IEnumerable<(ElementId QuestId, byte Index, ContentFinderConditionData Data)> GetAllQuestsWithQuestBattles()
    {
        return _questBattlesToContentFinderCondition.Select(x => (x.Key.QuestId, x.Key.Index, _contentFinderConditions[x.Value]));
    }

    private static string FixName(string name, ClientLanguage language)
    {
        if (string.IsNullOrEmpty(name) || language != ClientLanguage.English)
            return name;

        return string.Concat(name[0].ToString().ToUpper(CultureInfo.InvariantCulture), name.AsSpan(1));
    }

    private static IEnumerable<(ElementId QuestId, byte Index, uint QuestBattleId)> GetQuestBattles(Quest quest)
    {
        foreach (Quest.QuestParamsStruct t in quest.QuestParams)
        {
            if (t.ScriptInstruction == "QUESTBATTLE0")
                yield return (QuestId.FromRowId(quest.RowId), 0, t.ScriptArg);
            else if (t.ScriptInstruction == "QUESTBATTLE1")
                yield return (QuestId.FromRowId(quest.RowId), 1, t.ScriptArg);
            else if (t.ScriptInstruction.IsEmpty)
                break;
        }
    }

    private static uint LookupContentFinderConditionForQuestBattle(IDataManager dataManager, uint questBattleId)
    {
        if (questBattleId >= 5000)
            return dataManager.GetExcelSheet<InstanceContent>().GetRow(questBattleId).ContentFinderCondition.RowId;
        else
            return dataManager.GetExcelSheet<QuestBattleResident>().GetRow(questBattleId).Unknown0;
    }

    public sealed record ContentFinderConditionData(
        uint ContentFinderConditionId,
        string Name,
        uint TerritoryId,
        ushort RequiredItemLevel)
    {
        public ContentFinderConditionData(ContentFinderCondition condition, ClientLanguage clientLanguage)
            : this(condition.RowId, FixName(condition.Name.ToDalamudString().ToString(), clientLanguage),
                condition.TerritoryType.RowId, condition.ItemLevelRequired)
        {
        }
    }
}
