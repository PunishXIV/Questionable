using System.Text.Json;
using Questionable.Model.Questing;

namespace Questionable.Windows.PathEditorComponents;

[RegisterSingleton]
internal sealed class PathEditorSession(QuestRegistry questRegistry, QuestData questData)
{
    public ElementId? QuestId { get; private set; }
    public QuestInfo? Info { get; private set; }
    public QuestRoot? WorkingRoot { get; private set; }
    public int SelectedSequenceIndex { get; set; }
    public int SelectedStepIndex { get; set; }
    public bool Dirty { get; set; }

    public QuestSequence? SelectedSequence =>
        WorkingRoot != null && SelectedSequenceIndex >= 0 && SelectedSequenceIndex < WorkingRoot.QuestSequence.Count
            ? WorkingRoot.QuestSequence[SelectedSequenceIndex]
            : null;

    public QuestStep? SelectedStep =>
        SelectedSequence is { } sequence && SelectedStepIndex >= 0 && SelectedStepIndex < sequence.Steps.Count
            ? sequence.Steps[SelectedStepIndex]
            : null;

    public void Load(ElementId questId)
    {
        QuestId = questId;
        try
        {
            Info = questData.GetQuestInfo(questId) as QuestInfo;
        }
        catch (Exception)
        {
            Info = null;
        }

        if (questRegistry.TryGetQuest(questId, out Quest? quest))
        {
            WorkingRoot = Clone(quest.Root);
            Dirty = false;
        }
        else if (Info != null)
        {
            WorkingRoot = QuestRegistry.CreateQuestRoot(Info);
            Dirty = true;
        }
        else
            WorkingRoot = null;

        SelectedSequenceIndex = 0;
        SelectedStepIndex = 0;
    }

    public void Discard()
    {
        if (QuestId != null)
            Load(QuestId);
    }

    private static QuestRoot Clone(QuestRoot root)
    {
        return JsonSerializer.SerializeToNode(root, JsonOptions.Default)!.Deserialize<QuestRoot>()!;
    }
}
