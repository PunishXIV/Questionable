using Dalamud.Plugin.Ipc;
using JetBrains.Annotations;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class QuestionableIpc : IDisposable
{
    private const string IpcIsRunning = "Questionable.IsRunning";
    private const string IpcGetCurrentQuestId = "Questionable.GetCurrentQuestId";
    private const string IpcGetCurrentStepData = "Questionable.GetCurrentStepData";
    private const string IpcGetCurrentlyActiveEventQuests = "Questionable.GetCurrentlyActiveEventQuests";
    private const string IpcStartQuest = "Questionable.StartQuest";
    private const string IpcStartSingleQuest = "Questionable.StartSingleQuest";
    private const string IpcIsQuestLocked = "Questionable.IsQuestLocked";
    private const string IpcIsQuestLockedReason = "Questionable.IsQuestLockedReason";
    private const string IpcIsQuestComplete = "Questionable.IsQuestComplete";
    private const string IpcIsReadyToAcceptQuest = "Questionable.IsReadyToAcceptQuest";
    private const string IpcIsQuestAccepted = "Questionable.IsQuestAccepted";
    private const string IpcIsQuestUnobtainable = "Questionable.IsQuestUnobtainable";
    private const string IpcImportQuestPriority = "Questionable.ImportQuestPriority";
    private const string IpcClearQuestPriority = "Questionable.ClearQuestPriority";
    private const string IpcAddQuestPriority = "Questionable.AddQuestPriority";
    private const string IpcInsertQuestPriority = "Questionable.InsertQuestPriority";
    private const string IpcExportQuestPriority = "Questionable.ExportQuestPriority";
    private const string IpcStop = "Questionable.Stop";
    private const string IpcRedoLookup = "Questionable.RedoLookup";
    private const string IpcRedoLookupIndex = "Questionable.RedoLookupIndex";
    private const string IpcGetStopReason = "Questionable.GetStopReason";
    private const string IpcStartEventQuests = "Questionable.StartEventQuests";
    private readonly ICallGateProvider<string> _getStopReason;
    private readonly ICallGateProvider<bool> _startEventQuests;
    private readonly EventInfoComponent _eventInfo;
    private readonly ICallGateProvider<string, bool> _addQuestPriority;
    private readonly ICallGateProvider<bool> _clearQuestPriority;
    private readonly ICallGateProvider<string> _exportQuestPriority;
    private readonly ICallGateProvider<List<string>> _getActiveEventQuests;
    private readonly ICallGateProvider<string?> _getCurrentQuestId;
    private readonly ICallGateProvider<StepData?> _getCurrentStepData;
    private readonly ICallGateProvider<string, bool> _importQuestPriority;
    private readonly ICallGateProvider<int, string, bool> _insertQuestPriority;
    private readonly ICallGateProvider<string, bool> _isQuestAccepted;
    private readonly ICallGateProvider<string, bool> _isQuestComplete;
    private readonly ICallGateProvider<string, bool> _isQuestLocked;
    private readonly ICallGateProvider<string, (bool, string)> _isQuestLockedReason;
    private readonly ICallGateProvider<string, bool> _isQuestUnobtainable;
    private readonly ICallGateProvider<string, bool> _isReadyToAcceptQuest;

    private readonly ICallGateProvider<bool> _isRunning;
    private readonly ILogger<QuestionableIpc> _logger;

    private readonly QuestController _questController;
    private readonly QuestStarter _questStarter;
    private readonly QuestFunctions _questFunctions;
    private readonly QuestRegistry _questRegistry;
    private readonly ICallGateProvider<uint, string> _redoLookup;
    private readonly ICallGateProvider<uint, Tuple<string, int>> _redoLookupIndex;
    private readonly RedoUtil _redoUtil;
    private readonly ICallGateProvider<string, bool> _startQuest;
    private readonly ICallGateProvider<string, bool> _startSingleQuest;
    private readonly ICallGateProvider<string, bool> _stop;

    public QuestionableIpc(
        QuestController questController,
        QuestStarter questStarter,
        EventInfoComponent eventInfoComponent,
        QuestRegistry questRegistry,
        QuestFunctions questFunctions,
        RedoUtil redoUtil,
        ILogger<QuestionableIpc> logger,
        IDalamudPluginInterface pluginInterface)
    {
        _questController = questController;
        _questStarter = questStarter;
        _questRegistry = questRegistry;
        _questFunctions = questFunctions;
        _logger = logger;

        _isRunning = pluginInterface.GetIpcProvider<bool>(IpcIsRunning);
        _isRunning.RegisterFunc(() =>
            questController.AutomationType != QuestController.EAutomationType.Manual || questController.IsRunning);

        _getCurrentQuestId = pluginInterface.GetIpcProvider<string?>(IpcGetCurrentQuestId);
        _getCurrentQuestId.RegisterFunc(() => questController.CurrentQuest?.Quest.Id.ToString());

        _getCurrentStepData = pluginInterface.GetIpcProvider<StepData?>(IpcGetCurrentStepData);
        _getCurrentStepData.RegisterFunc(GetStepData);

        _getActiveEventQuests =
            pluginInterface.GetIpcProvider<List<string>>(IpcGetCurrentlyActiveEventQuests);
        _getActiveEventQuests.RegisterFunc(() =>
            [.. eventInfoComponent.GetActiveQuests().Select(q => q.ToString())]);

        _startQuest = pluginInterface.GetIpcProvider<string, bool>(IpcStartQuest);
        _startQuest.RegisterFunc(questId => StartQuest(questId, single: false));

        _startSingleQuest = pluginInterface.GetIpcProvider<string, bool>(IpcStartSingleQuest);
        _startSingleQuest.RegisterFunc(questId => StartQuest(questId, single: true));

        _isQuestLocked = pluginInterface.GetIpcProvider<string, bool>(IpcIsQuestLocked);
        _isQuestLocked.RegisterFunc(IsQuestLocked);

        _isQuestLockedReason = pluginInterface.GetIpcProvider<string, (bool, string)>(IpcIsQuestLockedReason);
        _isQuestLockedReason.RegisterFunc(IsQuestLockedReason);

        _isQuestComplete = pluginInterface.GetIpcProvider<string, bool>(IpcIsQuestComplete);
        _isQuestComplete.RegisterFunc(IsQuestComplete);

        _isReadyToAcceptQuest = pluginInterface.GetIpcProvider<string, bool>(IpcIsReadyToAcceptQuest);
        _isReadyToAcceptQuest.RegisterFunc(IsReadyToAcceptQuest);

        _isQuestAccepted = pluginInterface.GetIpcProvider<string, bool>(IpcIsQuestAccepted);
        _isQuestAccepted.RegisterFunc(IsQuestAccepted);

        _isQuestUnobtainable = pluginInterface.GetIpcProvider<string, bool>(IpcIsQuestUnobtainable);
        _isQuestUnobtainable.RegisterFunc(IsQuestUnobtainable);

        _importQuestPriority = pluginInterface.GetIpcProvider<string, bool>(IpcImportQuestPriority);
        _importQuestPriority.RegisterFunc(ImportQuestPriority);

        _addQuestPriority = pluginInterface.GetIpcProvider<string, bool>(IpcAddQuestPriority);
        _addQuestPriority.RegisterFunc(AddQuestPriority);

        _clearQuestPriority = pluginInterface.GetIpcProvider<bool>(IpcClearQuestPriority);
        _clearQuestPriority.RegisterFunc(ClearQuestPriority);

        _insertQuestPriority = pluginInterface.GetIpcProvider<int, string, bool>(IpcInsertQuestPriority);
        _insertQuestPriority.RegisterFunc(InsertQuestPriority);

        _exportQuestPriority = pluginInterface.GetIpcProvider<string>(IpcExportQuestPriority);
        _exportQuestPriority.RegisterFunc(() => QuestClipboard.Encode(questController.PriorityManager.Quests.Select(x => x.Id)));

        _stop = pluginInterface.GetIpcProvider<string, bool>(IpcStop);
        _stop.RegisterFunc(Stop);

        _redoUtil = redoUtil;

        _redoLookup = pluginInterface.GetIpcProvider<uint, string>(IpcRedoLookup);
        _redoLookup.RegisterFunc(RedoLookup);

        _redoLookupIndex = pluginInterface.GetIpcProvider<uint, Tuple<string, int>>(IpcRedoLookupIndex);
        _redoLookupIndex.RegisterFunc(RedoLookupIndex);

        _eventInfo = eventInfoComponent;
        _getStopReason = pluginInterface.GetIpcProvider<string>(IpcGetStopReason);
        _getStopReason.RegisterFunc(() => questController.StopReason);

        _startEventQuests = pluginInterface.GetIpcProvider<bool>(IpcStartEventQuests);
        _startEventQuests.RegisterFunc(StartEventQuests);
    }

    public void Dispose()
    {
        _isRunning.UnregisterFunc();
        _getCurrentQuestId.UnregisterFunc();
        _getCurrentStepData.UnregisterFunc();
        _getActiveEventQuests.UnregisterFunc();
        _startQuest.UnregisterFunc();
        _startSingleQuest.UnregisterFunc();
        _isQuestLocked.UnregisterFunc();
        _isQuestLockedReason.UnregisterFunc();
        _isQuestComplete.UnregisterFunc();
        _isReadyToAcceptQuest.UnregisterFunc();
        _isQuestAccepted.UnregisterFunc();
        _isQuestUnobtainable.UnregisterFunc();
        _importQuestPriority.UnregisterFunc();
        _addQuestPriority.UnregisterFunc();
        _clearQuestPriority.UnregisterFunc();
        _insertQuestPriority.UnregisterFunc();
        _exportQuestPriority.UnregisterFunc();
        _stop.UnregisterFunc();
        _redoLookup.UnregisterFunc();
        _redoLookupIndex.UnregisterFunc();
        _getStopReason.UnregisterFunc();
        _startEventQuests.UnregisterFunc();
    }

    private bool StartEventQuests()
    {
        _logger.LogDebug("StartEventQuests()");
        Quest? Next() => NextEventQuest(_eventInfo.GetActiveQuests(),
            id => _questRegistry.TryGetQuest(id, out Quest? quest) ? quest : null,
            id => _questFunctions.IsQuestAccepted(id), id => _questFunctions.IsReadyToAcceptQuest(id));

        return Next() is { } first && _questStarter.StartEventQuests(first, Next, "IPCEventQuests");
    }

    internal static Quest? NextEventQuest(IEnumerable<ElementId> active, Func<ElementId, Quest?> path,
        Func<ElementId, bool> accepted, Func<ElementId, bool> ready) =>
        active.Where(id => accepted(id) || ready(id)).Select(path).FirstOrDefault(quest => quest != null);

    private bool StartQuest(string questId, bool single)
    {
        _logger.LogDebug("StartQuest({QuestId},{Single})", questId, single);
        if (ElementId.TryFromString(questId, out ElementId? elementId) && elementId != null &&
            _questRegistry.TryGetQuest(elementId, out Quest? quest))
        {
            if (single && !_questFunctions.IsQuestAccepted(elementId) &&
                !_questFunctions.IsReadyToAcceptQuest(elementId))
            {
                _logger.LogWarning("Cannot start single quest {QuestId}: it is neither accepted nor ready to accept", questId);
                return false;
            }

            if (!_questStarter.CanStartQuest("IPCQuestSelection"))
                return false;

            _questController.SetNextQuest(quest);
            if (single)
                _questStarter.StartSingleQuest("IPCQuestSelection");
            else
                _questStarter.Start("IPCQuestSelection");
            return true;
        }

        return false;
    }

    private StepData? GetStepData()
    {
        _logger.LogDebug("GetStepData()");
        QuestController.QuestProgress? progress = _questController.CurrentQuest;
        if (progress == null)
            return null;

        string questId = progress.Quest.Id.ToString();
        if (string.IsNullOrEmpty(questId))
            return null;

        QuestStep? step = progress.FindStep();
        if (step == null)
            return null;

        return new()
        {
            QuestId = questId,
            Sequence = progress.Sequence,
            Step = progress.Step,
            InteractionType = step.InteractionType.ToString(),
            Position = step.Position,
            TerritoryId = step.TerritoryId
        };
    }

    private bool IsQuestLocked(string questId)
    {
        _logger.LogDebug("IsQuestLocked({QuestId})", questId);
        if (ElementId.TryFromString(questId, out ElementId? elementId) && elementId != null &&
            _questRegistry.TryGetQuest(elementId, out Quest? _))
        {
            (var isLocked, string[]? _) = _questFunctions.IsQuestLocked(elementId);
            return isLocked;
        }

        return true;
    }

    private (bool, string) IsQuestLockedReason(string questId)
    {
        _logger.LogDebug("IsQuestLockedReason({QuestId})", questId);
        if (ElementId.TryFromString(questId, out ElementId? elementId) && elementId != null &&
            _questRegistry.TryGetQuest(elementId, out Quest? _))
        {
            (var isLocked, string[]? reasons) = _questFunctions.IsQuestLocked(elementId);
            return (isLocked, reasons != null ? string.Join(',', reasons) : "");
        }

        return (true, "");
    }

    private bool IsQuestComplete(string questId)
    {
        _logger.LogDebug("IsQuestComplete({QuestId})", questId);
        if (ElementId.TryFromString(questId, out ElementId? elementId) && elementId != null)
            return _questFunctions.IsQuestComplete(elementId);
        return false;
    }

    private bool IsReadyToAcceptQuest(string questId)
    {
        _logger.LogDebug("IsReadyToAcceptQuest({QuestId})", questId);
        if (ElementId.TryFromString(questId, out ElementId? elementId) && elementId != null)
            return _questFunctions.IsReadyToAcceptQuest(elementId);
        return false;
    }

    private bool IsQuestAccepted(string questId)
    {
        _logger.LogDebug("IsQuestAccepted({QuestId})", questId);
        if (ElementId.TryFromString(questId, out ElementId? elementId) && elementId != null)
            return _questFunctions.IsQuestAccepted(elementId);
        return false;
    }

    private bool IsQuestUnobtainable(string questId)
    {
        _logger.LogDebug("IsQuestUnobtainable({QuestId})", questId);
        if (ElementId.TryFromString(questId, out ElementId? elementId) && elementId != null)
            return _questFunctions.IsQuestUnobtainable(elementId);
        return false;
    }

    private bool ImportQuestPriority(string encodedQuestPriority)
    {
        _logger.LogDebug("ImportQuestPriority({EncodedQuestPriority})", encodedQuestPriority);
        List<ElementId> questElements = QuestClipboard.Decode(encodedQuestPriority);
        _questController.PriorityManager.Import(questElements);
        return true;
    }

    private bool ClearQuestPriority()
    {
        _logger.LogDebug("ClearQuestPriority()");
        _questController.PriorityManager.Clear();
        return true;
    }

    private bool AddQuestPriority(string questId)
    {
        _logger.LogDebug("AddQuestPriority({QuestId})", questId);
        if (ElementId.TryFromString(questId, out ElementId? elementId) && elementId != null &&
            _questRegistry.IsKnownQuest(elementId))
        {
            return _questController.PriorityManager.Add(elementId);
        }

        return false;
    }

    private bool InsertQuestPriority(int index, string questId)
    {
        _logger.LogDebug("InsertQuestPriority({Index},{QuestId})", index, questId);
        if (ElementId.TryFromString(questId, out ElementId? elementId) && elementId != null &&
            _questRegistry.IsKnownQuest(elementId))
        {
            return _questController.PriorityManager.Insert(index, elementId);
        }

        return false;
    }

    private bool Stop(string label)
    {
        _logger.LogDebug("Stop({Label})", label);
        _questController.StopAll($"IPC: {label}", requested: true);
        return true;
    }

    private string RedoLookup(uint questId)
    {
        if (questId >= 131072)
            return "";
        return ChapterName(_redoUtil.GetChapter((ushort)questId));
    }

    internal static string ChapterName(RedoIndex redo) =>
        redo.Index == -1 ? "" : redo.Chapter.ChapterName.ToString();

    private Tuple<string, int> RedoLookupIndex(uint questId)
    {
        if (questId >= 131072)
            return new("", -1);
        RedoIndex redo = _redoUtil.GetChapter((ushort)questId);
        return new(ChapterName(redo), redo.Index);
    }

    [UsedImplicitly(ImplicitUseKindFlags.Access, ImplicitUseTargetFlags.WithMembers)]
    public sealed class StepData
    {
        public required string QuestId { get; init; }
        public required byte Sequence { get; init; }
        public required int Step { get; init; }
        public required string InteractionType { get; init; }
        public required Vector3? Position { get; init; }
        public required uint TerritoryId { get; init; }
    }
}
