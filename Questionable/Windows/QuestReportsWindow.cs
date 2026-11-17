namespace Questionable.Windows;

[RegisterSingleton]
internal sealed class QuestReportsWindow : LWindow
{
    private readonly ReportApi _api;
    private readonly Configuration _configuration;
    private readonly QuestRegistry _registry;
    private readonly QuestReporter _reporter;
    private View? _view;

    public QuestReportsWindow(ReportApi api, Configuration configuration, QuestRegistry registry, QuestReporter reporter)
        : base(_L("Community Reports") + "###QuestionableCommunityReports")
    {
        _api = api;
        _configuration = configuration;
        _registry = registry;
        _reporter = reporter;
        Size = new Vector2(700, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Open(IQuestInfo quest)
    {
        string? path = _registry.TryGetQuest(quest.QuestId, out Quest? data) ? _reporter.PathFor(data) : null;
        View view = new(quest.QuestId.ToString(), quest.Name, path, null, true, _L("Loading..."),
            data?.Source == Quest.ESource.ProjectDirectory);
        Volatile.Write(ref _view, view);
        IsOpen = true;
        string token = _configuration.Reports.AdminToken;
        _ = Load(view, token);
    }

    private async Task Load(View view, string token)
    {
        ReportApi.AdminPage? page = await _api.GetReportPageAsync(view.Quest, token).ConfigureAwait(false);
        View next = view with
        {
            Page = page,
            Busy = false,
            Message = page == null ? _L("Couldn't load the reports. Check your admin token and /xllog.") : null,
        };
        Interlocked.CompareExchange(ref _view, next, view);
    }

    private void Refresh(View view)
    {
        View loading = view with { Path = CurrentPath(view.Quest), Busy = true, Message = _L("Loading...") };
        if (ReferenceEquals(Interlocked.CompareExchange(ref _view, loading, view), view))
            _ = Load(loading, _configuration.Reports.AdminToken);
    }

    private void Change(View view, long? reportId)
    {
        if (view.Page == null || (reportId == null && view.Path == null))
            return;
        if (reportId == null && CurrentPath(view.Quest) != view.Path)
        {
            Interlocked.CompareExchange(ref _view, view with
            {
                Message = _L("The installed path changed. Refresh and test it before verifying."),
            }, view);
            return;
        }
        View busy = view with { Busy = true, Message = _L("Saving...") };
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _view, busy, view), view))
            return;
        string token = _configuration.Reports.AdminToken;
        _ = ChangeAsync(busy, token, reportId);
    }

    private string? CurrentPath(string quest) =>
        ElementId.TryFromString(quest, out ElementId? id) && id != null &&
        _registry.TryGetQuest(id, out Quest? data) ? _reporter.PathFor(data) : null;

    private async Task ChangeAsync(View view, string token, long? reportId)
    {
        bool success = reportId is { } id
            ? await _api.ResolveReportAsync(view.Quest, id, token).ConfigureAwait(false)
            : await _api.VerifyPathAsync(view.Quest, view.Path!, view.Page!.Snapshot, token).ConfigureAwait(false);
        if (success)
        {
            _reporter.Invalidate();
            await Load(view, token).ConfigureAwait(false);
        }
        else
            Interlocked.CompareExchange(ref _view, view with
            {
                Busy = false,
                Message = _L("Couldn't save the review. Refresh the reports and try again."),
            }, view);
    }

    public override void DrawContent()
    {
        View? view = Volatile.Read(ref _view);
        if (view == null)
            return;
        if (!_configuration.Advanced.Debug || string.IsNullOrWhiteSpace(_configuration.Reports.AdminToken))
        {
            ImGui.TextDisabled(_L("Enable Debug and configure your admin token to review reports."));
            return;
        }

        ImGui.TextUnformatted($"{view.Name} (#{view.Quest})");
        using (ImRaii.Disabled(view.Busy))
        {
            if (ImGui.Button(_L("Refresh")))
                Refresh(view);
            ImGui.SameLine();
            using (ImRaii.Disabled(view.Page == null || view.Path == null))
                if (ImGui.Button(_L("Verified working")))
                    Change(view, null);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(_L("Verify the currently installed path for 30 days and resolve problems recorded when this view was loaded. Later reports remain open. Only use this after testing the fix."));
        }
        if (view.Local)
            ImGui.TextColored(QstTheme.Amber,
                _L("This path is loaded from your project folder. Only verify it once the same version is published."));
        if (view.Message != null)
            ImGui.TextColored(QstTheme.TextMuted, view.Message);
        if (view.Page is not { } page)
            return;
        if (page.Reports.Count == 0)
            ImGui.TextDisabled(_L("No problem reports for this quest."));

        using ImRaii.TableDisposable table = ImRaii.Table("Reports", 6,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY);
        if (!table)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(_L("When"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(_L("Problem"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(_L("Step"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(_L("Path"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(_L("Review"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(_L("Versions and last actions"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (ReportApi.AdminReport report in page.Reports)
        {
            using var id = ImRaii.PushId(report.Id.ToString(CultureInfo.InvariantCulture));
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
            if (report.Path == view.Path && ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Currently installed path"));
            ImGui.TableNextColumn();
            if (report.ResolvedAt is { } resolved)
            {
                ImGui.TextDisabled(_L("Resolved"));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_LF("Resolved by {0} on {1}", report.ResolvedBy ?? "?",
                        DateTimeOffset.FromUnixTimeMilliseconds(resolved).LocalDateTime.ToString("g", CultureInfo.CurrentCulture)));
            }
            else if (report.Kind == "playerStopped")
                ImGui.TextDisabled(_L("Manual stop"));
            else if (!report.Current)
                ImGui.TextDisabled(_L("Previous report"));
            else
            {
                using var disabled = ImRaii.Disabled(view.Busy);
                if (ImGui.SmallButton(_L("Resolve")))
                    Change(view, report.Id);
            }
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

    private sealed record View(string Quest, string Name, string? Path, ReportApi.AdminPage? Page,
        bool Busy, string? Message, bool Local = false);
}
