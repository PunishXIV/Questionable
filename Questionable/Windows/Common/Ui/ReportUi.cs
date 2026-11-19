namespace Questionable.Windows.Common.Ui;

internal static class ReportUi
{
    public static Vector4 Color(ReportStatus status) => Color(status.State);

    public static Vector4 Color(EReportState state) => state switch
    {
        EReportState.Problems => QstTheme.Danger,
        EReportState.NeedsReview => QstTheme.Accent,
        EReportState.Working => QstTheme.Success,
        _ => QstTheme.TextMuted,
    };

    public static string Label(ReportStatus status) => status.Reason switch
    {
        EReportReason.Loading => _L("Loading reports..."),
        EReportReason.Unavailable => _L("Reports unavailable"),
        EReportReason.Disabled => _L("Reports disabled"),
        EReportReason.NoReports => _L("No reports"),
        EReportReason.OldReports => _L("Reports out of date"),
        EReportReason.FewReports => _L("Needs more reports"),
        EReportReason.Problems => _L("Reported issues"),
        EReportReason.Verified => _L("Verified working"),
        EReportReason.Worked => _L("Working"),
        _ => _L("Reports unavailable"),
    };

    public static string Description(ReportStatus status)
    {
        string text = Label(status);
        if (status.Reports is { } reports)
        {
            text += "\n" + _LF("Latest 30-day summary: {0} successful reports, {1} unresolved problems.",
                reports.RecentWorked, reports.RecentProblems);
            if (reports.LastReportAt is { } last)
                text += "\n" + _LF("Latest report: {0}", Date(last));
            if (reports.VerifiedAt is { } verified)
                text += "\n" + _LF("Verified working: {0}", Date(verified));
        }
        return text;
    }

    public static string GroupDescription(IReadOnlyList<(string Name, ReportStatus Status)> entries) =>
        _LF("{0} with issues, {1} needing review, {2} unknown, {3} working.",
            entries.Count(x => x.Status.State == EReportState.Problems),
            entries.Count(x => x.Status.State == EReportState.NeedsReview),
            entries.Count(x => x.Status.State == EReportState.Unknown),
            entries.Count(x => x.Status.State == EReportState.Working)) +
        "\n" + string.Join('\n', entries.Select(x => $"{x.Name}: {Label(x.Status)}"));

    private static string Date(long timestamp) =>
        DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
}
