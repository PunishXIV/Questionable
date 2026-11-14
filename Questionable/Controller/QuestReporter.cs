using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Quest = Questionable.Domain.Quest;
using QuestProgress = Questionable.Controller.QuestController.QuestProgress;

namespace Questionable.Controller;

// Quest results for the community counts (GitHub #9 and #10). Automatic reports need the player's yes;
// a click on the report button or chat link is consent for that one report.
[RegisterSingleton]
internal sealed class QuestReporter
{
    internal const int MinReports = 5;
    private const int MaxActions = 10;
    private const int MaxActionLength = 160;
    private const uint ReportLinkId = 1;
    private static readonly TimeSpan CountsMaxAge = TimeSpan.FromHours(1);

    // Plugins whose version helps reproduce a problem; the Worker accepts up to 12 entries.
    private static readonly string[] TrackedPlugins =
        ["BossMod", "vnavmesh", "TextAdvance", "Lifestream", "AutoDuty", "WrathCombo", "RotationSolver", "GatherChill",
            "Knightshopper", "WahTools"];

    private readonly Configuration _configuration;
    private readonly Func<ReportApi.Body, Task<bool>> _send;
    private readonly Func<Task<Dictionary<string, Dictionary<string, int[]>>?>> _fetchCounts;
    private readonly Func<string?> _character;
    private readonly ILogger _logger;
    private readonly IDalamudPluginInterface? _pluginInterface;
    private readonly IChatGui? _chatGui;
    private readonly DalamudLinkPayload? _reportLink;
    private readonly ConditionalWeakTable<QuestRoot, string> _fingerprints = new();
    private readonly HashSet<ElementId> _problemSent = [];
    private EProblemKind? _pending;
    private ReportApi.Body? _offered;
    private Dictionary<string, Dictionary<string, int[]>> _counts = [];
    private DateTime _countsCheckedAt = DateTime.MinValue;
    private int _fetching;

    public QuestReporter(Configuration configuration, ReportApi api, IDalamudPluginInterface pluginInterface,
        IChatGui chatGui, ILogger<QuestReporter> logger)
        : this(configuration, api.SendAsync, api.GetCountsAsync, () => ProfileController.CurrentContentId, logger,
            pluginInterface, chatGui)
    {
    }

    internal QuestReporter(Configuration configuration, Func<ReportApi.Body, Task<bool>> send,
        Func<Task<Dictionary<string, Dictionary<string, int[]>>?>> fetchCounts, Func<string?> character,
        ILogger logger, IDalamudPluginInterface? pluginInterface = null, IChatGui? chatGui = null)
    {
        _configuration = configuration;
        _send = send;
        _fetchCounts = fetchCounts;
        _character = character;
        _logger = logger;
        _pluginInterface = pluginInterface;
        _chatGui = chatGui;
        // The plugin-scoped chat service drops this handler when the plugin unloads.
        _reportLink = chatGui?.AddChatLinkHandler(ReportLinkId, (_, _) => SendOffered());
    }

    private bool Sharing => _configuration.Reports.Share == true;

    // Detection points call this right before the stop that follows.
    public void Problem(EProblemKind kind) => _pending = kind;

    public void Stopped(QuestProgress? at, string reason, IReadOnlyList<string> recentTasks)
    {
        EProblemKind? kind = StopKind(_pending, reason);
        _pending = null;
        if (kind == null || at == null ||
            Build(at.Quest, at.Sequence, at.Step, kind, recentTasks) is not { } body)
            return;

        _problemSent.Add(at.Quest.Id);
        if (Sharing)
            Send(body);
        else if (kind != EProblemKind.PlayerStopped)
            Offer(body);
    }

    // A quest QST finished; a quest that already had a problem this session isn't "worked".
    public void Completed(Quest quest)
    {
        if (_problemSent.Remove(quest.Id) || !Sharing ||
            Build(quest, null, null, null, []) is not { } body)
            return;

        Send(body);
    }

    public bool SendManual(QuestProgress at, EProblemKind? kind, IReadOnlyList<string> recentTasks)
    {
        if (Build(at.Quest, at.Sequence, at.Step, kind, recentTasks) is not { } body)
            return false;

        if (kind != null)
            _problemSent.Add(at.Quest.Id);
        Send(body);
        return true;
    }

    public (int Worked, int Total)? CountsFor(Quest quest)
    {
        if (!_configuration.Reports.ShowCounts)
            return null;

        RefreshCounts();
        if (!_counts.TryGetValue(quest.Id.ToString(), out Dictionary<string, int[]>? paths) ||
            Fingerprint(quest) is not { } path ||
            !paths.TryGetValue(path, out int[]? counts) || counts is not [int worked, int total] ||
            total < MinReports)
            return null;

        return (worked, total);
    }

    private static EProblemKind? StopKind(EProblemKind? pending, string reason) =>
        pending ?? (QuestStopPolicy.ExcludedStopReasons.Any(x => x.Equals(reason, StringComparison.OrdinalIgnoreCase))
            ? EProblemKind.PlayerStopped
            : null);

    // Only the published paths; drafts and local edits would skew the counts.
    internal static bool CanReport(Quest.ESource source, bool projectPaths) =>
        source is Quest.ESource.Assembly or Quest.ESource.DownloadedBundle ||
        (projectPaths && source == Quest.ESource.ProjectDirectory);

    internal static string Fingerprint(QuestRoot root) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(root)))[..16];

    // The Worker salts this again, so stored values can't be matched back to a character.
    internal static string CharacterHash(string contentId) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"qst-character:{contentId}")));

    private static string ApiName(EProblemKind kind) => kind switch
    {
        EProblemKind.Stuck => "stuck",
        EProblemKind.Deaths => "deaths",
        EProblemKind.QuestBattle => "questBattle",
        EProblemKind.MissingPath => "missingPath",
        EProblemKind.PlayerStopped => "playerStopped",
        EProblemKind.WrongTarget => "wrongTarget",
        _ => "other",
    };

    private ReportApi.Body? Build(Quest quest, byte? sequence, int? step, EProblemKind? kind,
        IReadOnlyList<string> recentTasks)
    {
        bool projectPaths = _configuration.Advanced.Debug && _configuration.Reports.ReportProjectPaths;
        if (!CanReport(quest.Source, projectPaths) || _character() is not { } character ||
            Fingerprint(quest) is not { } path)
            return null;

        string hash = CharacterHash(character);
        if (kind is not { } problem)
            return new ReportApi.Body(hash, quest.Id.ToString(), path, "worked");

        return new ReportApi.Body(hash, quest.Id.ToString(), path, "problem", ApiName(problem),
            sequence, step is >= 0 and <= 255 ? step : null, Versions(),
            [.. recentTasks.TakeLast(MaxActions).Select(Clean)]);
    }

    private string? Fingerprint(Quest quest)
    {
        string fingerprint = _fingerprints.GetValue(quest.Root, root =>
        {
            try
            {
                return Fingerprint(root);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not fingerprint the path of {Quest}", quest.Id);
                return string.Empty;
            }
        });
        return fingerprint.Length > 0 ? fingerprint : null;
    }

    private Dictionary<string, string> Versions()
    {
        Dictionary<string, string> versions = new(StringComparer.Ordinal)
        {
            ["Questionable"] = typeof(QuestReporter).Assembly.GetName().Version?.ToString() ?? "?",
            ["Paths"] = _configuration.PathData.InstalledDataVersion.ToString(CultureInfo.InvariantCulture),
        };

        if (_pluginInterface != null)
        {
            foreach (IExposedPlugin plugin in _pluginInterface.InstalledPlugins)
            {
                if (plugin.IsLoaded && TrackedPlugins.Contains(plugin.InternalName, StringComparer.Ordinal))
                    versions[plugin.InternalName] = plugin.Version.ToString();
            }
        }

        return versions;
    }

    private static string Clean(string text) =>
        string.Concat(text.Take(MaxActionLength).Select(c => c is >= ' ' and <= '~' ? c : '?'));

    private void Send(ReportApi.Body body) => _ = SendAsync(body);

    private async Task SendAsync(ReportApi.Body body)
    {
        try
        {
            if (await _send(body).ConfigureAwait(false))
                _logger.LogInformation("Sent quest report for {Quest}: {Outcome} {Kind}", body.Quest, body.Outcome, body.Kind);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not send the quest report for {Quest}", body.Quest);
        }
    }

    private void Offer(ReportApi.Body body)
    {
        if (_chatGui == null || _reportLink == null)
            return;

        _offered = body;
        SeString message = new SeStringBuilder()
            .AddText(_L("Help fix this quest's path: "))
            .Add(_reportLink)
            .AddUiForeground(_L("[Send a report]"), 500)
            .Add(RawPayload.LinkTerminator)
            .Build();
        _chatGui.Print(message, CommandHandler.MessageTag, CommandHandler.TagColor);
    }

    private void SendOffered()
    {
        if (_offered is not { } body)
            return;

        _offered = null;
        Send(body);
        _chatGui?.Print(_L("Thanks, your report was sent."), CommandHandler.MessageTag, CommandHandler.TagColor);
    }

    private void RefreshCounts()
    {
        if (DateTime.Now - _countsCheckedAt < CountsMaxAge || Interlocked.Exchange(ref _fetching, 1) == 1)
            return;

        _countsCheckedAt = DateTime.Now;
        _ = Task.Run(async () =>
        {
            try
            {
                if (await _fetchCounts().ConfigureAwait(false) is { } counts)
                    _counts = counts;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not load community quest results");
            }
            finally
            {
                Interlocked.Exchange(ref _fetching, 0);
            }
        });
    }
}

