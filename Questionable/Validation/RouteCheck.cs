namespace Questionable.Validation;

// Debug tool: asks vnav whether quest walk points are still needed, one zone at a time.
[RegisterSingleton]
internal sealed class RouteCheck : IDisposable
{
    private static readonly TimeSpan RouteTimeout = TimeSpan.FromSeconds(30);

    private const string Header =
        "Verdict\tKind\tQuest\tSequence\tStep\tFromSequence\tFromStep\tToStep\tDirect\tVia\tNear\tEndGap\tNote";

    private readonly QuestRegistry _questRegistry;
    private readonly QuestController _questController;
    private readonly MovementController _movementController;
    private readonly NavmeshIpc _navmeshIpc;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IChatGui _chatGui;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ZoneTravel _zoneTravel;
    private readonly ICondition _condition;
    private readonly ILogger<RouteCheck> _logger;
    private CancellationTokenSource? _cancel;

    public RouteCheck(QuestRegistry questRegistry, QuestController questController,
        MovementController movementController, NavmeshIpc navmeshIpc, IFramework framework,
        IClientState clientState, IChatGui chatGui, IDalamudPluginInterface pluginInterface,
        ZoneTravel zoneTravel, ICondition condition, ILogger<RouteCheck> logger)
    {
        _questRegistry = questRegistry;
        _questController = questController;
        _movementController = movementController;
        _navmeshIpc = navmeshIpc;
        _framework = framework;
        _clientState = clientState;
        _chatGui = chatGui;
        _pluginInterface = pluginInterface;
        _zoneTravel = zoneTravel;
        _condition = condition;
        _logger = logger;
    }

    public void Dispose()
    {
        _cancel?.Cancel();
        _cancel?.Dispose();
    }

    private string ReportFolder => Path.Combine(_pluginInterface.ConfigDirectory.FullName, "navcheck");

    private IEnumerable<WalkPoints.Spot> AllSpots() => _questRegistry.AllQuests.SelectMany(WalkPoints.Find);

    private void Print(string message) => _chatGui.Print(message, CommandHandler.MessageTag, CommandHandler.TagColor);

    public void PrintZones()
    {
        var zones = AllSpots()
            .GroupBy(x => x.Point.Step.TerritoryId)
            .Select(x => (Id: x.Key, Count: x.Count(), Done: File.Exists(ReportPath(x.Key))))
            .OrderByDescending(x => x.Count)
            .ToList();
        Print($"Route check: {zones.Count} zones, {zones.Count(x => x.Done)} checked, {zones.Sum(x => x.Count)} spots.");
        foreach (var zone in zones.Where(x => !x.Done))
        {
            string skip = _zoneTravel.Pick(zone.Id).Skip is { } reason ? $" ({reason})" : "";
            Print($"- {TerritoryData.GetNameAndId(zone.Id)}: {zone.Count}{skip}");
        }
    }

    public void Stop()
    {
        if (_cancel == null)
            Print("No route check is running.");
        else
            _cancel.Cancel();
    }

    public void Start()
    {
        if (!CanStart())
            return;

        uint territory = _clientState.TerritoryType;
        List<WalkPoints.Spot> spots = AllSpots().Where(x => x.Point.Step.TerritoryId == territory).ToList();
        if (spots.Count == 0)
        {
            Print($"No walk points to check in {TerritoryData.GetNameAndId(territory)}.");
            return;
        }

        _cancel = new CancellationTokenSource();
        Print($"Checking {spots.Count} spots in {TerritoryData.GetNameAndId(territory)}...");
        _ = Guarded(async token =>
        {
            if (await CheckZone(territory, spots, token).ConfigureAwait(false) is { } stop)
                await PrintOnFramework($"Route check stopped: {stop}").ConfigureAwait(false);
        }, _cancel.Token);
    }

    public void StartAll()
    {
        if (!CanStart())
            return;

        Dictionary<uint, List<WalkPoints.Spot>> all = AllSpots()
            .GroupBy(x => x.Point.Step.TerritoryId)
            .ToDictionary(x => x.Key, x => x.ToList());
        List<Zone> todo = ZoneTravel.Order(
            all.Where(x => !File.Exists(ReportPath(x.Key))).Select(x => new Zone(x.Key, _zoneTravel.Pick(x.Key), x.Value)),
            x => x.Id, x => x.Way, _clientState.TerritoryType);
        Dictionary<uint, int> spotCounts = all.ToDictionary(x => x.Key, x => x.Value.Count);
        if (todo.Count == 0)
        {
            Print($"Every zone has a report: {WriteCombined(spotCounts, [])}");
            return;
        }

        _cancel = new CancellationTokenSource();
        Print($"Touring {todo.Count} zones, {todo.Count(x => x.Way.Skip != null)} of them can't be reached. /qst navcheck stop stops it.");
        _ = Guarded(token => Tour(todo, spotCounts, token), _cancel.Token);
    }

    private sealed record Zone(uint Id, ZoneTravel.Way Way, List<WalkPoints.Spot> Spots);

    private bool CanStart()
    {
        if (_cancel != null)
        {
            Print("A route check is already running; /qst navcheck stop cancels it.");
            return false;
        }

        if (Busy() is { } reason)
        {
            _chatGui.PrintError(reason, CommandHandler.MessageTag, CommandHandler.TagColor);
            return false;
        }

        return true;
    }

    private string? Busy()
    {
        if (!_navmeshIpc.IsReady())
            return "vnavmesh isn't ready in this zone yet.";
        if (_questController.IsRunning || _movementController.IsPathRunning || _movementController.IsPathfinding)
            return "Stop Questionable and any movement first.";
        return null;
    }

    private string? TourStop()
    {
        if (!_clientState.IsLoggedIn)
            return "you logged out";
        if (_condition[ConditionFlag.BoundByDuty])
            return "you're in a duty";
        if (_questController.IsRunning)
            return "Questionable started";
        return null;
    }

    private async Task Tour(List<Zone> zones, Dictionary<uint, int> spotCounts, CancellationToken token)
    {
        Dictionary<uint, string> skipped = [];
        int checkedZones = 0;
        string? stopped = null;
        try
        {
            for (int i = 0; i < zones.Count; ++i)
            {
                Zone zone = zones[i];
                string label = $"[{i + 1}/{zones.Count}] {TerritoryData.GetNameAndId(zone.Id)}";
                stopped = await _framework.RunOnFrameworkThread(TourStop).ConfigureAwait(false);
                if (stopped != null)
                    break;

                string? skip = await _zoneTravel.Go(zone.Id, zone.Way, token).ConfigureAwait(false);
                if (skip == null && !await _zoneTravel.WaitForMesh(Print, token).ConfigureAwait(false))
                    skip = "vnavmesh not ready after 10 minutes";
                if (skip != null)
                {
                    skipped[zone.Id] = skip;
                    await PrintOnFramework($"{label}: skipped, {skip}").ConfigureAwait(false);
                    continue;
                }

                await PrintOnFramework($"{label}: checking {zone.Spots.Count} spots").ConfigureAwait(false);
                stopped = await CheckZone(zone.Id, zone.Spots, token).ConfigureAwait(false);
                if (stopped != null)
                    break;
                ++checkedZones;
            }
        }
        finally
        {
            string summary = WriteCombined(spotCounts, skipped);
            string state = stopped != null ? $"stopped ({stopped})" : token.IsCancellationRequested ? "cancelled" : "done";
            await _framework.RunOnFrameworkThread(() =>
                Print($"Route tour {state}: {checkedZones} zones checked, {skipped.Count} skipped. Summary: {summary}"))
                .ConfigureAwait(false);
        }
    }

    // Returns why the check stopped, or null once the zone's report is written.
    private async Task<string?> CheckZone(uint territory, List<WalkPoints.Spot> spots, CancellationToken token)
    {
        List<string> rows = [Header];
        Dictionary<WalkPoints.Verdict, int> counts = [];
        foreach (WalkPoints.Spot spot in spots)
        {
            string? stop = await _framework.RunOnFrameworkThread(() =>
                _clientState.TerritoryType != territory ? "you left the zone" : Busy()).ConfigureAwait(false);
            if (stop != null)
                return stop;

            (WalkPoints.Verdict verdict, string row) = await Check(spot, token).ConfigureAwait(false);
            counts[verdict] = counts.GetValueOrDefault(verdict) + 1;
            rows.Add(row);
        }

        Directory.CreateDirectory(ReportFolder);
        string path = ReportPath(territory);
        await File.WriteAllLinesAsync(path, rows, token).ConfigureAwait(false);
        string summary = string.Join(", ", counts.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}"));
        await PrintOnFramework($"Route check done: {summary}. Report: {path}").ConfigureAwait(false);
        return null;
    }

    private async Task Guarded(Func<CancellationToken, Task> work, CancellationToken token)
    {
        try
        {
            await work(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await PrintOnFramework("Route check cancelled.").ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Route check failed");
            await PrintOnFramework($"Route check stopped: {e.Message}").ConfigureAwait(false);
        }
        finally
        {
            await _framework.RunOnFrameworkThread(() =>
            {
                _cancel?.Dispose();
                _cancel = null;
            }).ConfigureAwait(false);
        }
    }

    private string WriteCombined(Dictionary<uint, int> spotCounts, Dictionary<uint, string> skipped)
    {
        Directory.CreateDirectory(ReportFolder);
        Dictionary<uint, IReadOnlyList<string>> reports = [];
        foreach (string file in Directory.EnumerateFiles(ReportFolder, "*.tsv"))
        {
            if (uint.TryParse(Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture,
                    out uint territory))
                reports[territory] = File.ReadAllLines(file);
        }

        File.WriteAllLines(Path.Combine(ReportFolder, "all.tsv"), Combine(reports, TerritoryData.GetNameAndId));
        string path = Path.Combine(ReportFolder, "summary.txt");
        File.WriteAllLines(path, Summary(spotCounts, reports, skipped, TerritoryData.GetNameAndId));
        return path;
    }

    internal static IEnumerable<string> Combine(IReadOnlyDictionary<uint, IReadOnlyList<string>> reports,
        Func<uint, string> name)
    {
        yield return "Zone\tZoneName\t" + Header;
        foreach ((uint zone, IReadOnlyList<string> lines) in reports.OrderBy(x => x.Key))
        {
            foreach (string line in lines.Skip(1).Where(x => x.Length > 0))
                yield return $"{zone}\t{name(zone)}\t{line}";
        }
    }

    internal static List<string> Summary(IReadOnlyDictionary<uint, int> spotCounts,
        IReadOnlyDictionary<uint, IReadOnlyList<string>> reports, IReadOnlyDictionary<uint, string> skipped,
        Func<uint, string> name)
    {
        static string Verdicts(IEnumerable<string> rows) => string.Join(", ", rows
            .Select(x => x.Split('\t')[0])
            .GroupBy(x => x)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key} {x.Count()}"));

        List<string> checkedZones = reports.Keys.Where(spotCounts.ContainsKey).Order()
            .Select(x => $"  {name(x)}: {Verdicts(reports[x].Skip(1).Where(y => y.Length > 0))}").ToList();
        List<string> skippedZones = skipped.Keys.Where(x => !reports.ContainsKey(x)).Order()
            .Select(x => $"  {name(x)}: {skipped[x]} ({spotCounts.GetValueOrDefault(x)} spots)").ToList();
        List<string> open = spotCounts.Keys.Where(x => !reports.ContainsKey(x) && !skipped.ContainsKey(x)).Order()
            .Select(x => $"  {name(x)}: {spotCounts[x]} spots").ToList();

        List<string> lines =
        [
            $"Checked {checkedZones.Count} of {spotCounts.Count} zones.",
            $"Verdicts: {Verdicts(reports.Where(x => spotCounts.ContainsKey(x.Key)).SelectMany(x => x.Value.Skip(1)).Where(x => x.Length > 0))}",
        ];
        foreach ((string title, List<string> zones) in new[]
                 {
                     ("Checked:", checkedZones), ("Skipped (check these by hand):", skippedZones),
                     ("Not checked yet:", open),
                 })
        {
            if (zones.Count == 0)
                continue;
            lines.Add("");
            lines.Add(title);
            lines.AddRange(zones);
        }

        return lines;
    }

    private Task PrintOnFramework(string message) => _framework.RunOnFrameworkThread(() => Print(message));

    private string ReportPath(uint territory) => Path.Combine(ReportFolder, $"{territory}.tsv");

    private async Task<(WalkPoints.Verdict, string)> Check(WalkPoints.Spot spot, CancellationToken token)
    {
        Vector3? start = spot.From?.Step.Position;
        Vector3 point = spot.Point.Step.Position!.Value;
        if (spot.Kind == WalkPoints.Kind.NoNavmesh)
        {
            List<Vector3> path = start is { } from ? await Route(from, point, spot.FlyPoint, token).ConfigureAwait(false) : [];
            WalkPoints.Verdict verdict = WalkPoints.JudgeNoNavmesh(spot, path);
            return (verdict, Row(verdict, spot,
                start is { } s ? WalkPoints.Length(s, path) : null,
                start is { } t ? Vector3.Distance(t, point) : null,
                null, WalkPoints.EndGap(spot.Point.Step, path)));
        }

        if (start is not { } a || spot.To?.Step.Position is not { } c)
            return (WalkPoints.Verdict.NoStart, Row(WalkPoints.Verdict.NoStart, spot, null, null, null, null));

        List<Vector3> direct = await Route(a, c, spot.FlyTo, token).ConfigureAwait(false);
        float firstLeg = spot.Point.Step.DisableNavmesh
            ? Vector3.Distance(a, point)
            : WalkPoints.Length(a, await Route(a, point, spot.FlyPoint, token).ConfigureAwait(false));
        float via = firstLeg + WalkPoints.Length(point, await Route(point, c, spot.FlyTo, token).ConfigureAwait(false));
        WalkPoints.Verdict result = WalkPoints.JudgeWalkPoint(spot, direct);
        return (result, Row(result, spot, WalkPoints.Length(a, direct), via,
            WalkPoints.Gap(point, a, direct), WalkPoints.EndGap(spot.To.Step, direct)));
    }

    private async Task<List<Vector3>> Route(Vector3 from, Vector3 to, bool fly, CancellationToken token)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(RouteTimeout);
        try
        {
            return await _framework.RunOnFrameworkThread(() => _navmeshIpc.Pathfind(from, to, fly, timeout.Token))
                .ConfigureAwait(false) ?? [];
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            _logger.LogDebug(e, "No route from {From} to {To}", from, to);
            return [];
        }
    }

    private static string Row(WalkPoints.Verdict verdict, WalkPoints.Spot spot, float? direct, float? via, float? near,
        float? endGap)
    {
        QuestStep step = spot.Point.Step;
        List<string> note = [];
        if (spot.GroundOnly) note.Add("GroundOnly");
        if (step.Fly == true) note.Add("Fly");
        if (step.Land == true) note.Add("Land");
        if (step.DisableNavmesh) note.Add("NoNavmesh");
        if (step.Mount != null) note.Add("Mount");
        if (step.Sprint != null) note.Add("Sprint");
        if (step.SkipConditions != null) note.Add("Skip");
        return string.Join('\t',
            verdict, spot.Kind, spot.Quest.Id, spot.Point.Sequence, spot.Point.Index,
            spot.From?.Sequence.ToString(CultureInfo.InvariantCulture) ?? "",
            spot.From?.Index.ToString(CultureInfo.InvariantCulture) ?? "",
            spot.To?.Index.ToString(CultureInfo.InvariantCulture) ?? "",
            Number(direct), Number(via), Number(near), Number(endGap), string.Join(',', note));
    }

    private static string Number(float? value) =>
        value is { } v && float.IsFinite(v) ? v.ToString("0.0", CultureInfo.InvariantCulture) : "";
}
