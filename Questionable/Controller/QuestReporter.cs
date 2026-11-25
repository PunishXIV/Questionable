using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Quest = Questionable.Domain.Quest;
using QuestProgress = Questionable.Controller.QuestController.QuestProgress;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class QuestReporter
{
    internal const int MinReports = 5;
    private const int MaxActions = 10;
    private const int MaxActionLength = 160;
    private const uint ReportLinkId = 1;
    private static readonly TimeSpan CountsMaxAge = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FreshWindow = TimeSpan.FromMinutes(15);

    // The Worker accepts up to 16 entries, including Questionable and Paths.
    internal static readonly string[] TrackedPlugins =
        ["BossMod", "vnavmesh", "TextAdvance", "Lifestream", "AutoDuty", "WrathCombo", "RotationSolver", "GatherChill",
            "Knightshopper", "WahTools", "MappingTheRealm"];

    private readonly Configuration _configuration;
    private readonly Func<ReportApi.Body, Task<bool>> _send;
    private readonly Func<bool, Task<ReportApi.StatusReply?>> _fetchStatus;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<string?> _character;
    private readonly ILogger _logger;
    private readonly IDalamudPluginInterface? _pluginInterface;
    private readonly IChatGui? _chatGui;
    private readonly DalamudLinkPayload? _reportLink;
    private readonly ConditionalWeakTable<QuestRoot, string> _fingerprints = new();
    private readonly HashSet<ElementId> _problemSent = [];
    private EProblemKind? _pending;
    private ReportApi.Body? _offered;
    private readonly object _statusLock = new();
    private ReportApi.StatusReply? _status;
    private EReportFetch _fetchState = EReportFetch.Loading;
    private DateTimeOffset _statusCheckedAt = DateTimeOffset.MinValue;
    private Task? _statusTask;
    private int _generation;
    private bool _freshStatus;
    private long _statusAt;
    private long _freshAt;
    private DateTimeOffset _freshUntil = DateTimeOffset.MinValue;

    public QuestReporter(Configuration configuration, ReportApi api, IDalamudPluginInterface pluginInterface,
        IChatGui chatGui, ILogger<QuestReporter> logger)
        : this(configuration, api.SendAsync, fresh => api.GetStatusAsync(fresh ? configuration.Reports.AdminToken : null),
            () => ProfileController.CurrentContentId, logger,
            pluginInterface, chatGui)
    {
    }

    internal QuestReporter(Configuration configuration, Func<ReportApi.Body, Task<bool>> send,
        Func<Task<ReportApi.StatusReply?>> fetchStatus, Func<string?> character,
        ILogger logger, IDalamudPluginInterface? pluginInterface = null, IChatGui? chatGui = null,
        Func<DateTimeOffset>? now = null)
        : this(configuration, send, _ => fetchStatus(), character, logger, pluginInterface, chatGui, now) { }

    internal QuestReporter(Configuration configuration, Func<ReportApi.Body, Task<bool>> send,
        Func<bool, Task<ReportApi.StatusReply?>> fetchStatus, Func<string?> character,
        ILogger logger, IDalamudPluginInterface? pluginInterface = null, IChatGui? chatGui = null,
        Func<DateTimeOffset>? now = null)
    {
        _configuration = configuration;
        _send = send;
        _fetchStatus = fetchStatus;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _character = character;
        _logger = logger;
        _pluginInterface = pluginInterface;
        _chatGui = chatGui;
        // The plugin-scoped chat service drops this handler when the plugin unloads.
        _reportLink = chatGui?.AddChatLinkHandler(ReportLinkId, (_, _) => SendOffered());
    }

    private bool Sharing => _configuration.Reports.Share == true;

    public void Problem(EProblemKind kind) => _pending = kind;

    public EProblemKind? Stopped(QuestProgress? at, string reason, IReadOnlyList<string> recentTasks)
    {
        EProblemKind? kind = StopKind(_pending, reason);
        _pending = null;
        if (kind == null || at == null ||
            Build(at.Quest, at.Sequence, at.Step, kind, recentTasks) is not { } body)
            return kind;

        _problemSent.Add(at.Quest.Id);
        if (Sharing)
            Send(body);
        else if (kind != EProblemKind.PlayerStopped)
            Offer(body);
        return kind;
    }

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

    public ReportStatus StatusFor(Quest quest)
    {
        if (!_configuration.Reports.ShowCounts)
            return ReportStatus.Evaluate(null, EReportFetch.Disabled, _now());

        _ = RefreshStatusAsync();
        lock (_statusLock)
        {
            if (_fetchState != EReportFetch.Ready)
                return ReportStatus.Evaluate(null, _fetchState, _now());
            if (PathFor(quest) is not { } path)
                return ReportStatus.Evaluate(null, EReportFetch.Failed, _now());

            ReportApi.PathReports? reports = null;
            if (_status?.Quests.TryGetValue(quest.Id.ToString(), out var paths) == true)
                paths.TryGetValue(path, out reports);
            return ReportStatus.Evaluate(reports, EReportFetch.Ready, _now());
        }
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
            PathFor(quest) is not { } path)
            return null;

        string hash = CharacterHash(character);
        if (kind is not { } problem)
            return new ReportApi.Body(hash, quest.Id.ToString(), path, "worked");

        return new ReportApi.Body(hash, quest.Id.ToString(), path, "problem", ApiName(problem),
            sequence, step is >= 0 and <= 255 ? step : null, Versions(),
            [.. recentTasks.TakeLast(MaxActions).Select(Clean)]);
    }

    public string? PathFor(Quest quest)
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
            {
                if (body.Outcome == "problem" && body.Kind != "playerStopped")
                    Invalidate();
                _logger.LogInformation("Sent quest report for {Quest}: {Outcome} {Kind}", body.Quest, body.Outcome, body.Kind);
            }
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

    public void Invalidate(bool fresh = true)
    {
        lock (_statusLock)
        {
            ++_generation;
            _statusTask = null;
            _statusCheckedAt = DateTimeOffset.MinValue;
            _freshStatus |= fresh;
            if (fresh)
                _freshUntil = _now() + FreshWindow;
        }
    }

    internal Task RefreshStatusAsync()
    {
        if (!_configuration.Reports.ShowCounts)
            return Task.CompletedTask;

        lock (_statusLock)
        {
            if (_statusTask != null)
                return _statusTask;
            TimeSpan maxAge = _fetchState == EReportFetch.Failed ? RetryDelay : CountsMaxAge;
            if (_now() - _statusCheckedAt < maxAge)
                return Task.CompletedTask;

            int generation = _generation;
            bool admin = _configuration.Reports.AdminToken.Length > 0;
            bool fresh = admin && (_freshStatus || _now() < _freshUntil);
            long newest = _statusAt;
            long freshAt = _freshAt;
            _statusCheckedAt = _now();
            _statusTask = Task.Run(async () =>
            {
                ReportApi.StatusReply? status = null;
                bool valid = false;
                bool needsFresh = fresh;
                try
                {
                    status = ReportApi.CleanStatus(await _fetchStatus(fresh).ConfigureAwait(false), _now());
                    valid = status != null;
                    if (valid && admin && !fresh && (status!.At < newest || status.At <= freshAt))
                    {
                        needsFresh = true;
                        status = ReportApi.CleanStatus(await _fetchStatus(true).ConfigureAwait(false), _now());
                        valid = status != null;
                    }
                    // Without fresh reads, an older edge cache can answer after a newer one.
                    valid &= status != null && (status.At >= newest || !admin);
                }
                catch (Exception e)
                {
                    valid = false;
                    _logger.LogWarning(e, "Could not load community quest results");
                }

                lock (_statusLock)
                {
                    if (generation != _generation)
                        return;
                    if (valid && status!.At < newest && _status != null)
                    {
                        _fetchState = EReportFetch.Ready;
                        _statusTask = null;
                        return;
                    }
                    _status = valid ? status : null;
                    _fetchState = valid ? EReportFetch.Ready : EReportFetch.Failed;
                    if (valid)
                    {
                        _freshStatus = false;
                        _statusAt = status!.At;
                        if (needsFresh)
                            _freshAt = Math.Max(_freshAt, status.At);
                        else if (status.At > _freshAt)
                            _freshAt = 0;
                    }
                    else
                        _freshStatus |= needsFresh;
                    _statusTask = null;
                }
            });
            return _statusTask;
        }
    }
}

