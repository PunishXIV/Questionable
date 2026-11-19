namespace Questionable.Controller;

internal static class SetAsideQuests
{
    private static readonly Dictionary<ElementId, byte> Quests = [];

    public static IReadOnlyDictionary<ElementId, byte> All => Quests;

    public static void Add(ElementId questId, byte sequence) => Quests[questId] = sequence;

    public static bool Remove(ElementId questId) => Quests.Remove(questId);

    public static void Clear() => Quests.Clear();

    public static bool Contains(ElementId? questId, byte sequence) =>
        questId != null && Quests.TryGetValue(questId, out byte setAt) && setAt == sequence;
}
