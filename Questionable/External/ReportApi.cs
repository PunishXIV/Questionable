using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Questionable.External;

// Client for the qst-reports Worker (cloudflare/report-api in this repo). Failures are logged, never thrown.
[RegisterSingleton]
internal sealed class ReportApi : IDisposable
{
    internal const string BaseUrl = "https://qst-reports.kagekazu.workers.dev";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly ILogger<ReportApi> logger;

    public ReportApi(ILogger<ReportApi> logger)
        : this(logger, new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(15) }) { }

    internal ReportApi(ILogger<ReportApi> logger, HttpClient http)
    {
        this.logger = logger;
        _http = http;
    }

    public async Task<bool> SendAsync(Body body)
    {
        try
        {
            using HttpResponseMessage response = await _http.PostAsJsonAsync("/v1/reports", body, Json).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return true;

            logger.LogWarning("Quest report for {Quest} was refused: {Status}", body.Quest, (int)response.StatusCode);
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Could not send the quest report for {Quest}: {Message}", body.Quest, e.Message);
            return false;
        }
    }

    public async Task<StatusReply?> GetStatusAsync(bool fresh = false)
    {
        try
        {
            StatusReply? reply = await _http.GetFromJsonAsync<StatusReply>(fresh ? "/v2/status?fresh=1" : "/v2/status", Json)
                .ConfigureAwait(false);
            return CleanStatus(reply);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Could not load community path status: {Message}", e.Message);
            return null;
        }
    }

    public async Task<AdminPage?> GetReportPageAsync(string quest, string token)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"/v2/admin/reports?quest={Uri.EscapeDataString(quest)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Admin reports for {Quest} were refused: {Status}", quest, (int)response.StatusCode);
                return null;
            }

            AdminPage? page = await response.Content.ReadFromJsonAsync<AdminPage>(Json).ConfigureAwait(false);
            long latest = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();
            return page is { Snapshot: >= 0, Reports: not null } && page.Reports.All(x =>
                x is { Id: > 0 } && ValidTime(x.At, latest) && ValidTime(x.ResolvedAt, latest)) ? page : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Could not load admin reports for {Quest}: {Message}", quest, e.Message);
            return null;
        }
    }

    public Task<bool> ResolveReportAsync(string quest, long id, string token) =>
        AdminActionAsync("/v2/admin/resolve", new { quest, id }, token);

    public Task<bool> VerifyPathAsync(string quest, string path, long snapshot, string token) =>
        AdminActionAsync("/v2/admin/verify", new { quest, path, snapshot }, token);

    private async Task<bool> AdminActionAsync<T>(string endpoint, T body, string token)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(body, options: Json);
            using HttpResponseMessage response = await _http.SendAsync(request).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return true;

            logger.LogWarning("Community report action was refused: {Status}", (int)response.StatusCode);
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Could not update community reports: {Message}", e.Message);
            return false;
        }
    }

    internal static StatusReply? CleanStatus(StatusReply? reply, DateTimeOffset? now = null)
    {
        long latest = (now ?? DateTimeOffset.UtcNow).AddMinutes(5).ToUnixTimeMilliseconds();
        if (reply is not { At: > 0, Quests: not null } || !ValidTime(reply.At, latest))
            return null;
        // Report writes and summary reads can be served by different Worker clocks.
        long evidenceLimit = Math.Min(latest, reply.At + (long)TimeSpan.FromMinutes(5).TotalMilliseconds);
        Dictionary<string, Dictionary<string, PathReports>> quests = [];
        foreach ((string quest, Dictionary<string, PathReports>? paths) in reply.Quests)
        {
            Dictionary<string, PathReports> kept = [];
            foreach ((string path, PathReports? reports) in paths ?? [])
            {
                if (reports != null && ValidPath(reports, evidenceLimit))
                    kept[path] = reports;
            }
            if (kept.Count > 0)
                quests[quest] = kept;
        }
        return reply with { Quests = quests };
    }

    private static bool ValidPath(PathReports reports, long evidenceLimit) =>
        reports.Worked >= 0 && reports.Total >= reports.Worked &&
        reports.RecentWorked >= 0 && reports.RecentWorked <= reports.Worked &&
        reports.RecentProblems >= 0 && reports.RecentProblems <= reports.Total - reports.Worked &&
        (reports.FifthWorkedAt == null || reports.Worked >= QuestReporter.MinReports) &&
        ValidTime(reports.LastReportAt, evidenceLimit) && ValidTime(reports.LastWorkedAt, evidenceLimit) &&
        ValidTime(reports.FifthWorkedAt, evidenceLimit) && ValidTime(reports.LastProblemAt, evidenceLimit) &&
        ValidTime(reports.VerifiedAt, evidenceLimit);

    private static bool ValidTime(long? at, long latest) =>
        at == null || (at is >= 0 and <= 253402300799999 && at <= latest);

    public void Dispose() => _http.Dispose();

    internal sealed record Body(
        string Character,
        string Quest,
        string Path,
        string Outcome,
        string? Kind = null,
        int? Sequence = null,
        int? Step = null,
        Dictionary<string, string>? Versions = null,
        List<string>? Actions = null);

    internal sealed record AdminReport(
        string Path,
        string Kind,
        int? Sequence,
        int? Step,
        Dictionary<string, string>? Versions,
        List<string>? Actions,
        long At,
        long Id = 0,
        long? ResolvedAt = null,
        string? ResolvedBy = null,
        bool Current = true);

    internal sealed record AdminPage(long Snapshot, List<AdminReport> Reports);

    internal sealed record StatusReply(long At, Dictionary<string, Dictionary<string, PathReports>> Quests);

    internal sealed record PathReports(int Worked, int Total, int RecentWorked, int RecentProblems,
        long? LastReportAt, long? LastWorkedAt, long? FifthWorkedAt, long? LastProblemAt, long? VerifiedAt);
}
