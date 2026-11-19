namespace Questionable.Controller;

internal sealed class DailiesPlan(IReadOnlyList<EAlliedSociety> societies)
{
    internal sealed record Entry(QuestId Id, EAlliedSociety Society, bool Daily, bool Accepted, bool Ready,
        EAlliedSocietyRank Rank, byte Sequence, uint Territory, Vector3? Position, bool CraftReady = false,
        string? BlockReason = null, bool KeepCurrent = false);

    private readonly EAlliedSociety[] _order = [.. societies];
    private readonly Dictionary<QuestId, int> _attempts = [];
    private readonly HashSet<QuestId> _seen = [];
    private readonly HashSet<QuestId> _accepted = [];
    private readonly HashSet<QuestId> _stories = [];
    private EAlliedSociety? _society;
    public HashSet<QuestId> Completed { get; } = [];
    public Dictionary<QuestId, string> Skipped { get; } = [];
    public Dictionary<QuestId, string> Blocked { get; } = [];
    public string? StopReason { get; private set; }

    public Entry? Choose(IReadOnlyList<Entry> entries, int allowances, uint territory, Vector3? position,
        ElementId? current = null)
    {
        List<Entry> available = [.. entries.Where(x => societies.Contains(x.Society) &&
            !Completed.Contains(x.Id) && !Skipped.ContainsKey(x.Id) &&
            _attempts.GetValueOrDefault(x.Id) < 2 && (x.Accepted || x.Ready || x.BlockReason != null))];
        foreach (Entry entry in available.Where(x => x.Accepted))
        {
            _seen.Add(entry.Id);
            _accepted.Add(entry.Id);
            Blocked.Remove(entry.Id);
        }

        foreach (Entry entry in available.Where(x => !x.Accepted))
        {
            if (entry.BlockReason != null)
                Blocked[entry.Id] = entry.BlockReason;
            else
                Blocked.Remove(entry.Id);
        }

        Entry? held = available.FirstOrDefault(x => x.Id == current && x.Accepted &&
            x.KeepCurrent && x.BlockReason == null);
        if (held != null)
            return Pick(held);

        Entry? story = available.FirstOrDefault(x => !x.Daily && x.Accepted && x.Id == current);
        story ??= Travel(available.Where(x => !x.Daily && (x.Accepted || x.Ready)), territory, position).FirstOrDefault();
        if (story != null)
        {
            _stories.Add(story.Id);
            if (story.BlockReason != null)
            {
                StopReason = story.BlockReason;
                return null;
            }
            return Pick(story);
        }

        List<QuestId> planned = DailiesController.Plan(societies,
            society => available.Where(x => x.Society == society && x.Daily && !x.Accepted && x.Ready && x.BlockReason == null)
                .Select(x => (x.Id, x.Rank)), _ => true, allowances);
        List<Entry> work = [.. available.Where(x => x.Daily && (x.Accepted || planned.Contains(x.Id)))];
        foreach (Entry blocked in work.Where(x => x.BlockReason != null).ToList())
        {
            Skipped[blocked.Id] = blocked.BlockReason!;
            work.Remove(blocked);
        }
        if (work.Count == 0)
            return null;

        // Ixal craft materials carry a timed buff.
        Entry? craft = Travel(work.Where(x => x.Accepted && x.CraftReady), territory, position).FirstOrDefault();
        if (craft != null)
            return Pick(craft);

        if (_society == null || !work.Any(x => x.Society == _society))
            _society = Travel(work, territory, position).First().Society;
        List<Entry> batch = [.. work.Where(x => x.Society == _society)];
        Entry? accept = Travel(batch.Where(x => !x.Accepted), territory, position).FirstOrDefault();
        if (accept != null)
            return Pick(accept);

        List<Entry> unfinished = [.. batch.Where(x => x.Sequence != 255)];
        List<Entry> stages = unfinished.Count > 0 ? unfinished : batch;
        uint zone = stages.Any(x => x.Territory == territory)
            ? territory
            : stages.GroupBy(x => x.Territory).OrderByDescending(x => x.Count()).First().Key;
        return Pick(Travel(stages.Where(x => x.Territory == zone), territory, position).First());
    }

    private IEnumerable<Entry> Travel(IEnumerable<Entry> entries, uint territory, Vector3? position) =>
        entries.OrderBy(x => x.Territory == territory ? 0 : 1)
            .ThenBy(x => x.Territory == territory && position is { } here && x.Position is { } there
                ? Vector3.DistanceSquared(here, there) : float.MaxValue)
            .ThenBy(x => Array.IndexOf(_order, x.Society));

    private Entry Pick(Entry entry)
    {
        _seen.Add(entry.Id);
        if (!entry.Daily)
            _stories.Add(entry.Id);
        return entry;
    }

    public void Accepted(ElementId id)
    {
        if (id is QuestId questId)
        {
            _seen.Add(questId);
            _accepted.Add(questId);
            Blocked.Remove(questId);
        }
    }

    public void Complete(ElementId id)
    {
        if (id is QuestId questId && _seen.Contains(questId))
            Completed.Add(questId);
    }

    public EDailiesFailure Fail(ElementId id, string reason)
    {
        if (id is not QuestId questId || !_seen.Contains(questId))
            return EDailiesFailure.Stop;
        if (_stories.Contains(questId))
        {
            StopReason = reason;
            return EDailiesFailure.Stop;
        }
        int attempt = _attempts.GetValueOrDefault(questId) + 1;
        _attempts[questId] = attempt;
        if (attempt == 1)
            return EDailiesFailure.Retry;
        if (_accepted.Contains(questId))
            Skipped[questId] = reason;
        else
            Blocked[questId] = reason;
        return EDailiesFailure.Skip;
    }

    public bool WasAccepted(ElementId id) => id is QuestId questId && _accepted.Contains(questId);

    public int FinishedDailies => Completed.Count(x => !_stories.Contains(x));
    public int FinishedStories => Completed.Count(_stories.Contains);
    public int Unfinished => _accepted.Count(x => !Completed.Contains(x));
}
