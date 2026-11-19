namespace Questionable.Windows.JournalComponents;

[RegisterSingleton]
internal sealed class QuestLinesComponent
{
    private const string Source = "QuestLines";

    private readonly QuestData _questData;
    private readonly QuestRegistry _questRegistry;
    private readonly UiUtils _uiUtils;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly QuestJournalUtils _questJournalUtils;
    private readonly QuestSelector _questSelector;
    private readonly Lazy<ILookup<ElementId, IQuestInfo>> _followUps;
    private IQuestInfo? _selected;

    public QuestLinesComponent(QuestData questData, QuestRegistry questRegistry, UiUtils uiUtils,
        QuestTooltipComponent questTooltipComponent, QuestJournalUtils questJournalUtils)
    {
        _questData = questData;
        _questRegistry = questRegistry;
        _uiUtils = uiUtils;
        _questTooltipComponent = questTooltipComponent;
        _questJournalUtils = questJournalUtils;
        _questSelector = new QuestSelector(questRegistry) { OnSelect = quest => _selected = quest.Info };
        _followUps = new(() => questData.GetAll()
            .SelectMany(quest => quest.PreviousQuests.Select(previous => (QuestId: (ElementId)previous.QuestId, Quest: quest)))
            .ToLookup(x => x.QuestId, x => x.Quest));
    }

    public void Draw()
    {
        _questSelector.DrawSelection();
        if (_selected == null)
        {
            ImGui.TextColored(QstTheme.TextMuted, _L("Pick a quest to see the quests before and after it."));
            return;
        }

        DrawRow(_selected);

        using (QstWidgets.TitledCard(_L("Needed first")))
            DrawChildren(Before(_selected), before: true);
        using (QstWidgets.TitledCard(_L("Unlocks next")))
            DrawChildren(_followUps.Value[_selected.QuestId].ToList(), before: false);
    }

    private List<IQuestInfo> Before(IQuestInfo questInfo) =>
        [.. questInfo.PreviousQuests.Select(x => _questData.TryGetQuestInfo(x.QuestId, out IQuestInfo? info) ? info : null)
            .OfType<IQuestInfo>()];

    private void DrawChildren(List<IQuestInfo> quests, bool before)
    {
        if (quests.Count == 0)
            ImGui.TextColored(QstTheme.TextMuted, _L("None"));

        foreach (IQuestInfo questInfo in quests)
            DrawNode(questInfo, before);
    }

    // Children are only looked up once a node is opened, so long story chains stay cheap.
    private void DrawNode(IQuestInfo questInfo, bool before)
    {
        using ImRaii.IdDisposable id = ImRaii.PushId(questInfo.QuestId.ToString());
        bool hasChildren = before ? questInfo.PreviousQuests.Count > 0 : _followUps.Value[questInfo.QuestId].Any();
        bool open = ImGui.TreeNodeEx("##Node",
            hasChildren ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);
        ImGui.SameLine();
        DrawRow(questInfo);

        if (!open || !hasChildren)
            return;

        DrawChildren(before ? Before(questInfo) : _followUps.Value[questInfo.QuestId].ToList(), before);
        ImGui.TreePop();
    }

    private void DrawRow(IQuestInfo questInfo)
    {
        if (_uiUtils.DrawQuestRow(questInfo.QuestId, $"{questInfo.Name} ({questInfo.QuestId})"))
            _questTooltipComponent.Draw(questInfo);

        _questRegistry.TryGetQuest(questInfo.QuestId, out Quest? quest);
        _questJournalUtils.ShowContextMenu(questInfo, quest, Source);
    }
}
