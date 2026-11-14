using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Questionable.External;

// Client for the qst-reports Worker (cloudflare/report-api in this repo). Failures are logged, never thrown.
[RegisterSingleton]
internal sealed class ReportApi(ILogger<ReportApi> logger) : IDisposable
{
    internal const string BaseUrl = "https://qst-reports.kagekazu.workers.dev";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http = new() { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(15) };

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

    public async Task<Dictionary<string, Dictionary<string, int[]>>?> GetCountsAsync()
    {
        try
        {
            CountsReply? reply = await _http.GetFromJsonAsync<CountsReply>("/v1/counts", Json).ConfigureAwait(false);
            return reply?.Quests;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Could not load community quest results: {Message}", e.Message);
            return null;
        }
    }

    public async Task<List<AdminReport>?> GetAdminReportsAsync(string quest, string token)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"/v1/admin/reports?quest={Uri.EscapeDataString(quest)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Admin reports for {Quest} were refused: {Status}", quest, (int)response.StatusCode);
                return null;
            }

            AdminReply? reply = await response.Content.ReadFromJsonAsync<AdminReply>(Json).ConfigureAwait(false);
            return reply?.Reports;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Could not load admin reports for {Quest}: {Message}", quest, e.Message);
            return null;
        }
    }

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
        long At);

    private sealed record CountsReply(Dictionary<string, Dictionary<string, int[]>>? Quests);

    private sealed record AdminReply(List<AdminReport>? Reports);
}
