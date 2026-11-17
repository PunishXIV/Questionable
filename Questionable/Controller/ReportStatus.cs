namespace Questionable.Controller;

internal sealed record ReportStatus(EReportState State, EReportReason Reason, bool IsAvailable,
    ReportApi.PathReports? Reports = null)
{
    public const int RecentDays = 30;
    public bool NeedsAttention => IsAvailable && State != EReportState.Working;

    public static ReportStatus Evaluate(ReportApi.PathReports? reports, EReportFetch fetch, DateTimeOffset now)
    {
        if (fetch != EReportFetch.Ready)
            return new(EReportState.Unknown, fetch switch
            {
                EReportFetch.Disabled => EReportReason.Disabled,
                EReportFetch.Failed => EReportReason.Unavailable,
                _ => EReportReason.Loading,
            }, false);

        long cutoff = now.AddDays(-RecentDays).ToUnixTimeMilliseconds();
        bool Recent(long? at) => at is { } time && time >= cutoff;

        if (reports == null || (reports.LastReportAt == null && reports.VerifiedAt == null))
            return new(EReportState.Unknown, EReportReason.NoReports, true, reports);
        if (Recent(reports.LastProblemAt))
            return new(EReportState.Problems, EReportReason.Problems, true, reports);
        if (Recent(reports.VerifiedAt))
            return new(EReportState.Working, EReportReason.Verified, true, reports);
        if (Recent(reports.FifthWorkedAt))
            return new(EReportState.Working, EReportReason.Worked, true, reports);

        return new(EReportState.NeedsReview,
            Recent(reports.LastReportAt) ? EReportReason.FewReports : EReportReason.OldReports, true, reports);
    }

    public static ReportStatus Worst(IEnumerable<ReportStatus> statuses) =>
        statuses.OrderByDescending(x => x.State switch
        {
            EReportState.Problems => 3,
            EReportState.NeedsReview => 2,
            EReportState.Unknown => 1,
            _ => 0,
        }).FirstOrDefault() ?? new(EReportState.Unknown, EReportReason.NoReports, false);
}

internal enum EReportState { Unknown, NeedsReview, Working, Problems }
internal enum EReportFetch { Loading, Failed, Disabled, Ready }
internal enum EReportReason { Loading, Unavailable, Disabled, NoReports, OldReports, FewReports, Problems, Verified, Worked }
