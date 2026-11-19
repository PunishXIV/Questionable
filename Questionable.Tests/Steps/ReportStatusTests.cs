// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using Questionable.Controller;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class ReportStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static long At(double daysAgo = 0) => Now.AddDays(-daysAgo).ToUnixTimeMilliseconds();
    private static ReportApi.PathReports Reports => new(5, 5, 5, 0, At(), At(), At(), null, null);
    private static ReportStatus Status(ReportApi.PathReports? reports) =>
        ReportStatus.Evaluate(reports, EReportFetch.Ready, Now);

    [Theory]
    [InlineData((int)EReportFetch.Loading, (int)EReportReason.Loading)]
    [InlineData((int)EReportFetch.Failed, (int)EReportReason.Unavailable)]
    [InlineData((int)EReportFetch.Disabled, (int)EReportReason.Disabled)]
    public void Unavailable_DoesNotQueue(int fetch, int reason)
    {
        ReportStatus status = ReportStatus.Evaluate(Reports, (EReportFetch)fetch, Now);
        Assert.Equal(EReportState.Unknown, status.State);
        Assert.Equal((EReportReason)reason, status.Reason);
        Assert.False(status.IsAvailable);
        Assert.False(status.NeedsAttention);
        Assert.Null(status.Reports);
    }

    [Fact]
    public void NoReports_QueuesOnlyAfterFetch()
    {
        Assert.False(ReportStatus.Evaluate(null, EReportFetch.Loading, Now).NeedsAttention);
        ReportStatus status = Status(null);
        Assert.Equal(EReportReason.NoReports, status.Reason);
        Assert.True(status.NeedsAttention);
    }

    [Fact]
    public void FiveRecentCharacters_AreWorking()
    {
        ReportStatus status = Status(Reports);
        Assert.Equal(EReportReason.Worked, status.Reason);
        Assert.Equal(EReportState.Working, status.State);
        Assert.False(status.NeedsAttention);
    }

    [Fact]
    public void FourRecentCharacters_NeedReview()
    {
        ReportStatus status = Status(Reports with { RecentWorked = 4, FifthWorkedAt = At(31) });
        Assert.Equal(EReportReason.FewReports, status.Reason);
        Assert.True(status.NeedsAttention);
    }

    [Fact]
    public void CachedCount_ExpiresAtFifthDate()
    {
        ReportApi.PathReports reports = Reports with { FifthWorkedAt = At(30) };
        Assert.Equal(EReportState.Working, Status(reports).State);
        Assert.Equal(EReportState.NeedsReview,
            ReportStatus.Evaluate(reports, EReportFetch.Ready, Now.AddMilliseconds(1)).State);
    }

    [Fact]
    public void Problem_WinsOverVerification()
    {
        ReportStatus status = Status(Reports with { LastProblemAt = At(), VerifiedAt = At() });
        Assert.Equal(EReportState.Problems, status.State);
        Assert.True(status.NeedsAttention);
    }

    [Fact]
    public void ConcurrentProblem_StillWins()
    {
        ReportStatus status = Status(Reports with { LastProblemAt = At(0.001), VerifiedAt = At() });
        Assert.Equal(EReportState.Problems, status.State);
    }

    [Fact]
    public void OldProblem_DoesNotBlockWorking()
    {
        Assert.Equal(EReportState.Working, Status(Reports with { LastProblemAt = At(31) }).State);
    }

    [Fact]
    public void OldReports_NeedReview()
    {
        ReportStatus status = Status(Reports with
        {
            LastReportAt = At(31), LastWorkedAt = At(31), FifthWorkedAt = At(31), RecentWorked = 0
        });
        Assert.Equal(EReportReason.OldReports, status.Reason);
        Assert.True(status.NeedsAttention);
    }

    [Fact]
    public void Verification_WorksWithoutReports()
    {
        ReportApi.PathReports reports = new(0, 0, 0, 0, null, null, null, null, At(30));
        Assert.Equal(EReportReason.Verified, Status(reports).Reason);
        Assert.Equal(EReportReason.OldReports,
            ReportStatus.Evaluate(reports, EReportFetch.Ready, Now.AddMilliseconds(1)).Reason);
    }

    [Fact]
    public void Worst_UsesAgreedPriority()
    {
        ReportStatus working = Status(Reports);
        ReportStatus unknown = Status(null);
        ReportStatus review = Status(Reports with { FifthWorkedAt = null });
        ReportStatus problems = Status(Reports with { LastProblemAt = At() });
        Assert.Same(unknown, ReportStatus.Worst([working, unknown]));
        Assert.Same(review, ReportStatus.Worst([unknown, review, working]));
        Assert.Same(problems, ReportStatus.Worst([working, problems, review, unknown]));
        Assert.False(ReportStatus.Worst([]).NeedsAttention);
    }
}
