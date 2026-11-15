using System.Text.RegularExpressions;

namespace Questionable.Validation;

// Walk points that may only exist because older vnav couldn't route to the next step by itself.
internal static class WalkPoints
{
    public enum Kind
    {
        WalkPoint,
        NoNavmesh,
    }

    public enum Verdict
    {
        Same,
        Other,
        NoRoute,
        NoStart,
        Reachable,
        Detour,
    }

    public sealed record StepRef(byte Sequence, int Index, QuestStep Step);

    public sealed record Spot(Quest Quest, Kind Kind, StepRef Point, StepRef? From, StepRef? To)
    {
        // Ground routes for characters without flying; judging them as flights would be wrong.
        public bool GroundOnly => Point.Step.SkipConditions?.StepIf?.Flying == ELockedSkipCondition.Unlocked;
        public bool FlyTo => !GroundOnly && To?.Step.Fly == true;
        public bool FlyPoint => !GroundOnly && Point.Step.Fly == true;
    }

    private static readonly Regex Steering = new(
        "aggro|combat|enem|mob|avoid|navmesh|stuck|path|jump|ledge|drop|fall|wall|door|stair|bridge",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public const float MinGap = 15;

    public static bool IsCandidate(QuestStep step, QuestStep? next) =>
        step is
        {
            InteractionType: EInteractionType.WalkTo, Disabled: false, Position: { } at,
            AetheryteShortcut: null, AethernetShortcut: null, TargetTerritoryId: null, DataId: null,
            DelaySecondsAtStart: null,
        } &&
        step.RequiredQuestVariables.Count == 0 &&
        step.CompletionQuestVariablesFlags.Count == 0 &&
        !(step.Comment is { } comment && Steering.IsMatch(comment)) &&
        next is { Position: { } nextAt } &&
        next.TerritoryId == step.TerritoryId &&
        next.InteractionType != EInteractionType.Jump &&
        Vector3.Distance(at, nextAt) >= MinGap;

    public static IEnumerable<Spot> Find(Quest quest)
    {
        List<StepRef> all = [];
        List<bool> candidate = [];
        foreach (QuestSequence sequence in quest.AllSequences())
        {
            List<StepRef> steps = sequence.Steps
                .Select((step, index) => new StepRef(sequence.Sequence, index, step))
                .Where(x => !x.Step.Disabled)
                .ToList();
            for (int i = 0; i < steps.Count; ++i)
            {
                all.Add(steps[i]);
                candidate.Add(IsCandidate(steps[i].Step, i + 1 < steps.Count ? steps[i + 1].Step : null));
            }
        }

        for (int i = 0; i < all.Count; ++i)
        {
            QuestStep step = all[i].Step;
            if (candidate[i])
            {
                StepRef? to = null;
                for (int j = i + 1; j < all.Count && all[j].Sequence == all[i].Sequence; ++j)
                {
                    if (!candidate[j])
                    {
                        to = all[j];
                        break;
                    }
                }

                yield return new Spot(quest, Kind.WalkPoint, all[i], Before(all, candidate, i, true), to);
            }
            else if (step is { DisableNavmesh: true, Position: not null } &&
                     step.InteractionType != EInteractionType.WalkTo)
                yield return new Spot(quest, Kind.NoNavmesh, all[i], Before(all, candidate, i, false), null);
        }
    }

    private static StepRef? Before(List<StepRef> all, List<bool> candidate, int index, bool skipCandidates)
    {
        uint territory = all[index].Step.TerritoryId;
        for (int j = index - 1; j >= 0; --j)
        {
            if (skipCandidates && candidate[j])
                continue;
            if (all[j].Step.Position == null)
                continue;
            return all[j].Step.TerritoryId == territory ? all[j] : null;
        }

        return null;
    }

    public static float Length(Vector3 start, IReadOnlyList<Vector3> path)
    {
        float length = 0;
        Vector3 last = start;
        foreach (Vector3 point in path)
        {
            length += Vector3.Distance(last, point);
            last = point;
        }

        return length;
    }

    public static float Gap(Vector3 point, Vector3 start, IReadOnlyList<Vector3> path)
    {
        float best = Vector3.Distance(point, start);
        Vector3 last = start;
        foreach (Vector3 next in path)
        {
            best = Math.Min(best, SegmentGap(point, last, next));
            last = next;
        }

        return best;
    }

    private static float SegmentGap(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared == 0 ? 0 : Math.Clamp(Vector3.Dot(point - a, ab) / lengthSquared, 0, 1);
        return Vector3.Distance(point, a + ab * t);
    }

    public static float EndGap(QuestStep target, IReadOnlyList<Vector3> path) =>
        path.Count == 0 ? float.PositiveInfinity : Vector3.Distance(path[^1], target.Position!.Value);

    public static bool Reaches(QuestStep target, IReadOnlyList<Vector3> path) =>
        EndGap(target, path) <= Math.Max(target.StopDistance ?? QuestStep.DefaultStopDistance, 3);

    public static Verdict JudgeWalkPoint(Spot spot, IReadOnlyList<Vector3> direct)
    {
        if (spot.From == null || spot.To == null)
            return Verdict.NoStart;
        if (!Reaches(spot.To.Step, direct))
            return Verdict.NoRoute;
        float near = spot.FlyTo || spot.FlyPoint ? 10 : 5;
        return Gap(spot.Point.Step.Position!.Value, spot.From.Step.Position!.Value, direct) <= near
            ? Verdict.Same
            : Verdict.Other;
    }

    public static Verdict JudgeNoNavmesh(Spot spot, IReadOnlyList<Vector3> path)
    {
        if (spot.From == null)
            return Verdict.NoStart;
        if (!Reaches(spot.Point.Step, path))
            return Verdict.NoRoute;
        Vector3 start = spot.From.Step.Position!.Value;
        float straight = Vector3.Distance(start, spot.Point.Step.Position!.Value);
        return Length(start, path) <= straight * 1.3f + 2 ? Verdict.Reachable : Verdict.Detour;
    }
}
