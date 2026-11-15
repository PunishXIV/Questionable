namespace Questionable.Validation;

// Debug tool: moves the character between zones for the route check tour.
[RegisterSingleton]
internal sealed class ZoneTravel
{
    private static readonly TimeSpan IdleWait = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ArrivalWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HopWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan MeshWait = TimeSpan.FromMinutes(10);

    private readonly AetheryteData _aetheryteData;
    private readonly AetheryteFunctions _aetheryteFunctions;
    private readonly LifestreamIpc _lifestreamIpc;
    private readonly NavmeshIpc _navmeshIpc;
    private readonly GameFunctions _gameFunctions;
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly IFramework _framework;

    public ZoneTravel(AetheryteData aetheryteData, AetheryteFunctions aetheryteFunctions, LifestreamIpc lifestreamIpc,
        NavmeshIpc navmeshIpc, GameFunctions gameFunctions, IClientState clientState, ICondition condition,
        IFramework framework)
    {
        _aetheryteData = aetheryteData;
        _aetheryteFunctions = aetheryteFunctions;
        _lifestreamIpc = lifestreamIpc;
        _navmeshIpc = navmeshIpc;
        _gameFunctions = gameFunctions;
        _clientState = clientState;
        _condition = condition;
        _framework = framework;
    }

    public sealed record Way(EAetheryteLocation? Aetheryte, EAetheryteLocation? Shard, string? Skip);

    public Way Pick(uint territory) =>
        Pick(territory, _aetheryteData.TerritoryIds, _aetheryteData.AethernetGroups,
            _aetheryteFunctions.IsAetheryteUnlocked, _lifestreamIpc.GetVersion() != null);

    public static Way Pick(uint territory, IReadOnlyDictionary<EAetheryteLocation, uint> territories,
        IReadOnlyDictionary<EAetheryteLocation, ushort> groups, Func<EAetheryteLocation, bool> unlocked,
        bool hasLifestream)
    {
        List<EAetheryteLocation> here = territories.Where(x => x.Value == territory).Select(x => x.Key).Order().ToList();
        if (here.Count == 0)
            return new Way(null, null, "no aetheryte");

        foreach (EAetheryteLocation main in here.Where(x => !x.IsAethernetShard()))
        {
            if (unlocked(main))
                return new Way(main, null, null);
        }

        bool needsLifestream = false;
        foreach (EAetheryteLocation shard in here.Where(x => x.IsAethernetShard()))
        {
            if (!unlocked(shard) || !groups.TryGetValue(shard, out ushort group))
                continue;
            EAetheryteLocation? city = territories.Keys
                .Where(x => !x.IsAethernetShard() && groups.TryGetValue(x, out ushort g) && g == group && unlocked(x))
                .Order()
                .Cast<EAetheryteLocation?>()
                .FirstOrDefault();
            if (city == null)
                continue;
            if (!hasLifestream)
            {
                needsLifestream = true;
                continue;
            }

            return new Way(city, shard, null);
        }

        return new Way(null, null, needsLifestream ? "needs Lifestream" : "not attuned");
    }

    // The zone you stand in first, then by aetheryte so a region's zones follow each other.
    public static List<T> Order<T>(IEnumerable<T> zones, Func<T, uint> territory, Func<T, Way> way, uint current) =>
        zones.OrderBy(x => territory(x) != current)
            .ThenBy(x => way(x).Skip != null)
            .ThenBy(x => way(x).Aetheryte)
            .ThenBy(x => way(x).Shard)
            .ToList();

    // Returns why the zone was skipped, or null once the character stands in it.
    public async Task<string?> Go(uint territory, Way way, CancellationToken token)
    {
        if (await OnFramework(() => _clientState.TerritoryType == territory).ConfigureAwait(false))
            return null;
        if (way.Skip is { } skip)
            return skip;

        EAetheryteLocation aetheryte = way.Aetheryte!.Value;
        uint aetheryteTerritory = _aetheryteData.TerritoryIds[aetheryte];
        if (!await OnFramework(() => _clientState.TerritoryType == aetheryteTerritory).ConfigureAwait(false) &&
            !await Teleport(aetheryte, aetheryteTerritory, token).ConfigureAwait(false))
            return "teleport failed";
        if (way.Shard is { } shard && !await Hop(shard, territory, token).ConfigureAwait(false))
            return "aethernet failed";
        return null;
    }

    private async Task<bool> Teleport(EAetheryteLocation aetheryte, uint destination, CancellationToken token)
    {
        for (int attempt = 0; attempt < 3; ++attempt)
        {
            if (!await WaitFor(() => !_condition[ConditionFlag.InCombat] && !_gameFunctions.IsOccupied() &&
                                     _aetheryteFunctions.CanTeleport(aetheryte), IdleWait, token).ConfigureAwait(false))
                throw new TimeoutException("still in combat or busy after 2 minutes");

            if (!await OnFramework(() => _aetheryteFunctions.TeleportAetheryte(aetheryte)).ConfigureAwait(false))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                continue;
            }

            if (await WaitFor(() => _clientState.TerritoryType == destination && !_gameFunctions.IsOccupied(),
                    ArrivalWait, token).ConfigureAwait(false))
                return true;
        }

        return false;
    }

    private async Task<bool> Hop(EAetheryteLocation shard, uint destination, CancellationToken token)
    {
        for (int attempt = 0; attempt < 2; ++attempt)
        {
            await WaitFor(() => !_gameFunctions.IsOccupied() && !_lifestreamIpc.IsBusy, IdleWait, token)
                .ConfigureAwait(false);
            if (!await OnFramework(() => TryHop(shard)).ConfigureAwait(false))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                continue;
            }

            if (await WaitFor(() => _clientState.TerritoryType == destination && !_gameFunctions.IsOccupied() &&
                                    !_lifestreamIpc.IsBusy, HopWait, token).ConfigureAwait(false))
                return true;
        }

        return false;
    }

    private bool TryHop(EAetheryteLocation shard)
    {
        try
        {
            return _lifestreamIpc.Teleport(shard);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // vnav may still report the last zone's mesh for a moment after arriving.
    public async Task<bool> WaitForMesh(Action<string> print, CancellationToken token)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
        long start = TimeProvider.System.GetTimestamp();
        TimeSpan nextNote = TimeSpan.FromSeconds(30);
        while (!await OnFramework(_navmeshIpc.IsReady).ConfigureAwait(false))
        {
            TimeSpan waited = TimeProvider.System.GetElapsedTime(start);
            if (waited >= MeshWait)
                return false;
            if (waited >= nextNote)
            {
                int progress = await OnFramework(_navmeshIpc.GetBuildProgress).ConfigureAwait(false);
                await _framework.RunOnFrameworkThread(() => print($"Waiting for vnavmesh: {progress}%"))
                    .ConfigureAwait(false);
                nextNote += TimeSpan.FromSeconds(30);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<bool> WaitFor(Func<bool> done, TimeSpan limit, CancellationToken token)
    {
        long start = TimeProvider.System.GetTimestamp();
        while (!await OnFramework(done).ConfigureAwait(false))
        {
            if (TimeProvider.System.GetElapsedTime(start) >= limit)
                return false;
            await Task.Delay(TimeSpan.FromMilliseconds(500), token).ConfigureAwait(false);
        }

        return true;
    }

    private Task<T> OnFramework<T>(Func<T> read) => _framework.RunOnFrameworkThread(read);
}
