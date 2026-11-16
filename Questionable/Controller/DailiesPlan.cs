// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
namespace Questionable.Controller;

internal sealed class DailiesPlan(IReadOnlyList<EAlliedSociety> societies)
{
    internal sealed record Entry(QuestId Id, EAlliedSociety Society, bool Daily, bool Accepted, bool Ready,
        EAlliedSocietyRank Rank, byte Sequence, uint Territory, Vector3? Position, bool CraftReady = false,
        string? BlockReason = null);

    private readonly Dictionary<QuestId, int> _attempts = [];
    private readonly HashSet<QuestId> _seen = [];
    private readonly HashSet<QuestId> _stories = [];
    private EAlliedSociety? _society;
    public HashSet<QuestId> Completed { get; } = [];
    public Dictionary<QuestId, string> Skipped { get; } = [];
    public string? StopReason { get; private set; }

    public Entry? Choose(IReadOnlyList<Entry> entries, int allowances, uint territory, Vector3? position,
        ElementId? current = null)
    {
        List<Entry> available = [.. entries.Where(x => societies.Contains(x.Society) &&
            !Completed.Contains(x.Id) && !Skipped.ContainsKey(x.Id) && (x.Accepted || x.Ready))];
        foreach (Entry entry in available.Where(x => x.Accepted))
            _seen.Add(entry.Id);

        // Finish a rank-up already underway; ordinary dailies may change at sequence boundaries.
        Entry? story = available.FirstOrDefault(x => !x.Daily && x.Accepted && x.Id == current);
        story ??= Travel(available.Where(x => !x.Daily), territory, position).FirstOrDefault();
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

        // Preference order allocates allowances; route order never changes who receives them.
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

        // Ixal craft materials carry a timed buff; do not leave them waiting for another trip.
        Entry? craft = Travel(work.Where(x => x.Accepted && x.CraftReady), territory, position).FirstOrDefault();
        if (craft != null)
            return Pick(craft);

        if (_society == null || !work.Any(x => x.Society == _society))
            _society = Travel(work, territory, position).First().Society;
        List<Entry> batch = [.. work.Where(x => x.Society == _society)];
        Entry? accept = Travel(batch.Where(x => !x.Accepted), territory, position).FirstOrDefault();
        if (accept != null)
            return Pick(accept);

        // Finish work before the shared turn-in, grouping stages in the same zone.
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
            .ThenBy(x => Array.IndexOf(societies.ToArray(), x.Society));

    private Entry Pick(Entry entry)
    {
        _seen.Add(entry.Id);
        return entry;
    }

    public void Accepted(ElementId id)
    {
        if (id is QuestId questId)
            _seen.Add(questId);
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
        Skipped[questId] = reason;
        return EDailiesFailure.Skip;
    }

    public int FinishedDailies => Completed.Count(x => !_stories.Contains(x));
    public int FinishedStories => Completed.Count(_stories.Contains);
    public int Unfinished => _seen.Count(x => !Completed.Contains(x));
}
