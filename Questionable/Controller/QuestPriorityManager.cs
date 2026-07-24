using Questionable.Model.Questing;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller;

/// <summary>
///     Holds and mutates the user's manually-prioritised quest list. Extracted from
///     <see cref="QuestController"/> so the list logic lives in one focused, testable place.
/// </summary>
internal sealed class QuestPriorityManager(
    QuestRegistry questRegistry,
    ILogger<QuestPriorityManager> logger,
    IChatGui chatGui)
{
    private const char ClipboardSeparator = ';';
    private readonly List<Quest> _quests = [];

    public IReadOnlyList<Quest> Quests => _quests;
    public int Count => _quests.Count;
    public bool IsEmpty => _quests.Count == 0;

    public bool Contains(Quest quest) => _quests.Any(q => q.Id == quest.Id);

    public bool Contains(ElementId elementId) => _quests.Any(q => q.Id == elementId);

    public bool Add(Quest quest)
    {
        if (_quests.Contains(quest))
            return false;

        _quests.Add(quest);
        return true;
    }

    public bool Add(ElementId elementId)
    {
        if (questRegistry.TryGetQuest(elementId, out Quest? quest) && !_quests.Contains(quest))
            _quests.Add(quest);

        return true;
    }

    public bool Insert(int index, ElementId elementId)
    {
        try
        {
            if (questRegistry.TryGetQuest(elementId, out Quest? quest) && !_quests.Contains(quest))
                _quests.Insert(index, quest);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to insert quest in priority list");
            chatGui.PrintError("Failed to insert quest in priority list, please check /xllog for details.",
                CommandHandler.MessageTag, CommandHandler.TagColor);
            return false;
        }
    }

    public void Remove(Quest quest) => Remove(quest.Id);

    public void Remove(ElementId elementId)
    {
        int index = _quests.FindIndex(q => q.Id == elementId);
        if (index != -1)
        {
            if (index >= _quests.Count)
                index = 0;
            logger.LogDebug($"Removing {index}: {_quests[index].Info.Name}");
            _quests.RemoveAt(index);
        }
    }

    /// <summary>Moves the quest at <paramref name="oldIndex"/> to <paramref name="newIndex"/> (used by drag-drop).</summary>
    public void Move(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= _quests.Count || newIndex < 0 || newIndex >= _quests.Count)
            return;

        Quest quest = _quests[oldIndex];
        _quests.RemoveAt(oldIndex);
        _quests.Insert(newIndex, quest);
    }

    public void RemoveCompleted(Func<ElementId, bool> isComplete) => _quests.RemoveAll(q => isComplete(q.Id));

    public void Clear() => _quests.Clear();

    public void Import(IEnumerable<ElementId> questElements)
    {
        foreach (ElementId elementId in questElements)
        {
            if (questRegistry.TryGetQuest(elementId, out Quest? quest) && !_quests.Contains(quest))
                _quests.Add(quest);
        }
    }

    public string Export() => string.Join(ClipboardSeparator, _quests.Select(x => x.Id.ToString()));
}
