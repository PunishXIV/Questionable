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

    /// <summary>
    ///     Quests in the priority list that should only be <em>accepted</em>, not driven to completion.
    ///     They are initially automated to acceptance; once accepted they are left to the normal questing rules (to-do list order, same-society batch turn-ins, ...)
    ///     to complete. They remain visible in the priority list, and marked as accepted, but the priority completion selection always skips them once accepted.
    /// </summary>
    private readonly HashSet<ElementId> _acceptOnly = [];

    public IReadOnlyList<Quest> Quests => _quests;
    public int Count => _quests.Count;
    public bool IsEmpty => _quests.Count == 0;

    /// <summary>Priority-list quests flagged accept-only, in list order.</summary>
    public IEnumerable<Quest> AcceptOnlyQuests => _quests.Where(q => _acceptOnly.Contains(q.Id));

    public bool HasPendingAcceptOnly => _acceptOnly.Count > 0;

    public bool IsAcceptOnly(ElementId elementId) => _acceptOnly.Contains(elementId);

    /// <summary>Adds the quest to the priority list (if needed) and flags it as accept-only.</summary>
    public void MarkAcceptOnly(ElementId elementId)
    {
        Add(elementId);
        if (Contains(elementId))
            _acceptOnly.Add(elementId);
    }

    public void ClearAcceptOnly(ElementId elementId) => _acceptOnly.Remove(elementId);

    /// <summary>Clears every accept-only flag (the quests remain in the priority list).</summary>
    public void ClearAllAcceptOnly() => _acceptOnly.Clear();

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

        _acceptOnly.Remove(elementId);
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

    public void RemoveCompleted(Func<ElementId, bool> isComplete, Func<ElementId, bool> isAccepted)
    {
        // First remove everything that's completed (any kind), then remove accept-only quests that have
        // been accepted (picked up) but not completed. Normal priority quests that are merely accepted and
        // still in progress are left alone. Keeps the accept-only flags in sync with whatever remains.
        _quests.RemoveAll(q => isComplete(q.Id));
        _quests.RemoveAll(q => _acceptOnly.Contains(q.Id) && isAccepted(q.Id));
        _acceptOnly.RemoveWhere(id => _quests.All(q => q.Id != id));
    }

    public void Clear()
    {
        _quests.Clear();
        _acceptOnly.Clear();
    }

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
