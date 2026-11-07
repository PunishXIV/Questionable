using System.Diagnostics.CodeAnalysis;
using ECommons.ExcelServices;
using Lumina.Excel.Sheets;
using Questionable.Model.Questing;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Functions;

// Quest data QuestFunctions looks up: sheet info, loaded paths and the ClassJob sheet.
internal interface IQuestCatalog
{
    bool TryGetQuestInfo(ElementId elementId, [NotNullWhen(true)] out IQuestInfo? questInfo);
    IQuestInfo GetQuestInfo(ElementId elementId);
    IReadOnlyList<QuestInfo> MainScenarioQuests { get; }
    QuestId LastMainScenarioQuestId { get; }
    IReadOnlyCollection<QuestId> GetLockedClassQuests();

    bool TryGetQuest(ElementId questId, [NotNullWhen(true)] out Quest? quest);
    bool IsKnownQuest(ElementId questId);
    IReadOnlyList<(uint CfcId, ElementId QuestId, int Sequence)> LowPriorityCfcQuests { get; }
    IReadOnlyList<QuestInfo> GetKnownClassJobQuests(Job classJob);

    (ushort UnlockQuestId, int ExpArrayIndex) GetClassJobUnlock(Job classJob);
}

[RegisterSingleton<IQuestCatalog, QuestCatalog>]
internal sealed class QuestCatalog(QuestData questData, QuestRegistry questRegistry, IDataManager dataManager) : IQuestCatalog
{
    public bool TryGetQuestInfo(ElementId elementId, [NotNullWhen(true)] out IQuestInfo? questInfo) =>
        questData.TryGetQuestInfo(elementId, out questInfo);

    public IQuestInfo GetQuestInfo(ElementId elementId) => questData.GetQuestInfo(elementId);
    public IReadOnlyList<QuestInfo> MainScenarioQuests => questData.MainScenarioQuests;
    public QuestId LastMainScenarioQuestId => questData.LastMainScenarioQuestId;
    public IReadOnlyCollection<QuestId> GetLockedClassQuests() => questData.GetLockedClassQuests();

    public bool TryGetQuest(ElementId questId, [NotNullWhen(true)] out Quest? quest) => questRegistry.TryGetQuest(questId, out quest);
    public bool IsKnownQuest(ElementId questId) => questRegistry.IsKnownQuest(questId);

    public IReadOnlyList<(uint CfcId, ElementId QuestId, int Sequence)> LowPriorityCfcQuests =>
        questRegistry.LowPriorityCfcQuests;

    public IReadOnlyList<QuestInfo> GetKnownClassJobQuests(Job classJob) => questRegistry.GetKnownClassJobQuests(classJob);

    public (ushort UnlockQuestId, int ExpArrayIndex) GetClassJobUnlock(Job classJob)
    {
        ClassJob row = dataManager.GetExcelSheet<ClassJob>().GetRow((uint)classJob);
        return ((ushort)row.UnlockQuest.RowId, row.ExpArrayIndex);
    }
}
