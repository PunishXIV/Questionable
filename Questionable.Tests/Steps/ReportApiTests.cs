// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class ReportApiTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request);
    }

    private static ReportApi Api(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(NullLogger<ReportApi>.Instance, new HttpClient(new Handler(send)) { BaseAddress = new Uri(ReportApi.BaseUrl) });

    private static Task<HttpResponseMessage> Response(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    [Fact]
    public async Task Status_UsesVersionTwo()
    {
        using ReportApi api = Api(request =>
        {
            Assert.Equal("/v2/status", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            return Response("""{"at":1794823200000,"quests":{}}""");
        });
        Assert.Empty((await api.GetStatusAsync())!.Quests);
    }

    [Theory]
    [InlineData("{\"quests\":{\"1190\":{\"abcd\":[5,5]}}}")]
    [InlineData("{\"at\":253402300799999,\"quests\":{}}")]
    public async Task OldOrInvalidReply_IsUnavailable(string body)
    {
        using ReportApi api = Api(_ => Response(body));
        Assert.Null(await api.GetStatusAsync());
    }

    [Theory]
    [InlineData("{\"at\":1794823200000,\"quests\":{\"1190\":null}}")]
    [InlineData("{\"at\":1794823200000,\"quests\":{\"1190\":{\"abcd\":null}}}")]
    public async Task NullRow_IsDropped(string body)
    {
        using ReportApi api = Api(_ => Response(body));
        Assert.Empty((await api.GetStatusAsync())!.Quests);
    }

    [Fact]
    public async Task OldWorker_DoesNotImplySuccess()
    {
        using ReportApi api = Api(_ => Response("not found", HttpStatusCode.NotFound));
        Assert.Null(await api.GetStatusAsync());
    }

    [Fact]
    public async Task BadTimestamp_DropsOnlyThatRow()
    {
        using ReportApi api = Api(_ => Response("""
            {"at":1794823200000,"quests":{"1190":{"abcd":{
              "worked":5,"total":5,"recentWorked":5,"recentProblems":0,
              "lastReportAt":9223372036854775807,"lastWorkedAt":1794823200000,
              "fifthWorkedAt":1794823200000,"lastProblemAt":null,"verifiedAt":null
            }},"1191":{"abcd":{
              "worked":1,"total":1,"recentWorked":1,"recentProblems":0,
              "lastReportAt":1794823200000,"lastWorkedAt":1794823200000,
              "fifthWorkedAt":null,"lastProblemAt":null,"verifiedAt":null
            }}}}
            """));
        Assert.Equal(["1191"], (await api.GetStatusAsync())!.Quests.Keys);
    }

    [Fact]
    public void FutureEvidence_IsRejected()
    {
        var now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        long at = now.ToUnixTimeMilliseconds();
        var reply = new ReportApi.StatusReply(at, new()
        {
            ["1190"] = new() { ["abcdef0123456789"] = new(0, 0, 0, 0, null, null, null, null, at + 300001) }
        });
        Assert.Empty(ReportApi.CleanStatus(reply, now)!.Quests);
    }

    [Fact]
    public async Task FreshStatus_BypassesServerCache()
    {
        using ReportApi api = Api(request =>
        {
            Assert.Equal("/v2/status?fresh=1", request.RequestUri!.PathAndQuery);
            return Response("""{"at":1794823200000,"quests":{}}""");
        });
        Assert.NotNull(await api.GetStatusAsync(fresh: true));
    }

    [Fact]
    public async Task AdminPage_PreservesSnapshot()
    {
        using ReportApi api = Api(request =>
        {
            Assert.Equal("/v2/admin/reports?quest=1190", request.RequestUri!.PathAndQuery);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization.Parameter);
            return Response("""
                {"snapshot":42,"reports":[{"id":12,"path":"abcdef0123456789","kind":"stuck",
                 "sequence":1,"step":2,"versions":{},"actions":[],"at":1794823200000,
                 "resolvedAt":1794823200001,"resolvedBy":"tester","current":false}]}
                """);
        });
        ReportApi.AdminPage? page = await api.GetReportPageAsync("1190", "test-token");
        Assert.Equal(42, page!.Snapshot);
        ReportApi.AdminReport report = Assert.Single(page.Reports);
        Assert.Equal(12, report.Id);
        Assert.False(report.Current);
        Assert.NotNull(report.ResolvedAt);
    }

    [Fact]
    public async Task Verify_SendsCapturedSnapshot()
    {
        using ReportApi api = Api(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v2/admin/verify", request.RequestUri!.AbsolutePath);
            Assert.Equal("test-token", request.Headers.Authorization!.Parameter);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("1190", body.RootElement.GetProperty("quest").GetString());
            Assert.Equal("abcdef0123456789", body.RootElement.GetProperty("path").GetString());
            Assert.Equal(42, body.RootElement.GetProperty("snapshot").GetInt64());
            Assert.False(body.RootElement.TryGetProperty("token", out _));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        Assert.True(await api.VerifyPathAsync("1190", "abcdef0123456789", 42, "test-token"));
    }

    [Fact]
    public async Task Resolve_RefusalIsNotSuccess()
    {
        using ReportApi api = Api(async request =>
        {
            Assert.Equal("/v2/admin/resolve", request.RequestUri!.AbsolutePath);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(12, body.RootElement.GetProperty("id").GetInt64());
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });
        Assert.False(await api.ResolveReportAsync("1190", 12, "test-token"));
    }

    [Fact]
    public async Task NetworkFailure_ReturnsUnavailable()
    {
        using ReportApi api = Api(_ => throw new HttpRequestException("offline"));
        Assert.Null(await api.GetStatusAsync());
        Assert.Null(await api.GetReportPageAsync("1190", "test-token"));
        Assert.False(await api.ResolveReportAsync("1190", 1, "test-token"));
    }
}
