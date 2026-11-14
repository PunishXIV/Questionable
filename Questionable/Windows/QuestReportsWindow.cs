namespace Questionable.Windows;

// Problem reports for one quest, for admins with a token (Advanced, Debug on).
[RegisterSingleton]
internal sealed class QuestReportsWindow : LWindow
{
    private readonly ReportApi _api;
    private readonly Configuration _configuration;
    private string _quest = string.Empty;
    private string _questName = string.Empty;
    private List<ReportApi.AdminReport>? _reports;
    private string? _status;

    public QuestReportsWindow(ReportApi api, Configuration configuration)
        : base(_L("Community Reports") + "###QuestionableCommunityReports")
    {
        _api = api;
        _configuration = configuration;
        Size = new Vector2(700, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Open(IQuestInfo quest)
    {
        string questId = quest.QuestId.ToString();
        _quest = questId;
        _questName = quest.Name;
        _reports = null;
        _status = _L("Loading...");
        IsOpen = true;

        string token = _configuration.Reports.AdminToken;
        _ = Task.Run(async () =>
        {
            List<ReportApi.AdminReport>? reports = await _api.GetAdminReportsAsync(questId, token).ConfigureAwait(false);
            if (questId != _quest)
                return;

            _reports = reports;
            _status = reports == null
                ? _L("Couldn't load the reports. Check your admin token and /xllog.")
                : reports.Count == 0 ? _L("No problem reports for this quest.") : null;
        });
    }

    public override void DrawContent()
    {
        ImGui.TextUnformatted($"{_questName} (#{_quest})");
        if (_status != null || _reports is not { } reports)
        {
            ImGui.TextColored(QstTheme.TextMuted, _status ?? string.Empty);
            return;
        }

        using ImRaii.TableDisposable table = ImRaii.Table("Reports", 5,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY);
        if (!table)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(_L("When"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(_L("Problem"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(_L("Step"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(_L("Path"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(_L("Versions and last actions"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (ReportApi.AdminReport report in reports)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(DateTimeOffset.FromUnixTimeMilliseconds(report.At).LocalDateTime
                .ToString("g", CultureInfo.CurrentCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(report.Kind);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(report.Sequence is { } sequence
                ? $"{sequence}.{report.Step?.ToString(CultureInfo.InvariantCulture) ?? "?"}"
                : "-");
            ImGui.TableNextColumn();
            ImGui.TextColored(QstTheme.TextMuted, report.Path);
            ImGui.TableNextColumn();
            if (report.Versions is { } versions)
                ImGui.TextWrapped(string.Join(", ", versions.Select(x => $"{x.Key} {x.Value}")));
            if (report.Actions is { Count: > 0 } actions)
            {
                ImGui.TextColored(QstTheme.TextMuted, _LF("{0} last actions (hover)", actions.Count));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(string.Join("\n", actions));
            }
        }
    }
}
