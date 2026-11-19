using Lumina.Excel.Sheets;
using Questionable.Controller.Steps.Interactions;
namespace Questionable.Windows.ConfigComponents;

[RegisterSingleton]
internal sealed class DutyConfigComponent : ConfigComponent
{
    private readonly AutoDutyProvider _autoDuty;
    private readonly Dictionary<EExpansionVersion, List<DutyInfo>> _dutiesByExpansion;
    private bool _useAutoDuty;

    private readonly QuestRegistry _questRegistry;
    private readonly QuestBattleConfigComponent _questBattles;
    private Vector2 _contentSize;

    public DutyConfigComponent(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        IDataManager dataManager,
        QuestRegistry questRegistry,
        AutoDutyProvider autoDuty,
        TerritoryData territoryData,
        QuestBattleConfigComponent questBattles)
        : base(pluginInterface, configuration)
    {
        _questRegistry = questRegistry;
        _autoDuty = autoDuty;
        _questBattles = questBattles;

        _dutiesByExpansion = dataManager.GetExcelSheet<DawnContent>()
            .Where(x => x is { RowId: > 0, Unknown16: false })
            .OrderBy(x => x.Unknown15) // SortKey for the support UI
            .Select(x => x.Content.ValueNullable)
            .Where(x => x != null)
            .Select(x => x!.Value)
            .Select(x => new
            {
                Expansion = (EExpansionVersion)x.TerritoryType.Value.ExVersion.RowId,
                CfcId = x.RowId,
                Name = territoryData.GetCfc(x.RowId)?.Name ?? _L("?"),
                TerritoryId = x.TerritoryType.RowId,
                ContentType = (EContentType)x.ContentType.RowId,
                Level = x.ClassJobLevelRequired,
                x.SortKey
            })
            .GroupBy(x => x.Expansion)
            .ToDictionary(x => x.Key,
                x => x
                    .Select(y => new DutyInfo(y.CfcId, y.TerritoryId, $"{FormatLevel(y.Level)} {y.Name}", y.ContentType))
                    .ToList());
    }

    public void DrawQuestBattles() => _questBattles.DrawSection();

    public void DrawDuties()
    {
        _contentSize = ImGui.GetWindowContentRegionMax();
        using var wrap = ImRaii.TextWrapPos(_contentSize.X + 10);

        _useAutoDuty = Configuration.Duties.RunInstancedContentWithAutoDuty;
        // The old label stays as the ImGui ID.
        if (ImGui.Checkbox(_L("Run duties with AutoDuty") + "###Run instanced content with AutoDuty and BossMod", ref _useAutoDuty))
        {
            Configuration.Duties.RunInstancedContentWithAutoDuty = _useAutoDuty;
            Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            _L("AutoDuty runs the duty with its own settings, including which rotation plugin to use. The combat plugin picked on the Basics page isn't used."));

        bool setAside = Configuration.Duties.SetAsideManualDuties;
        if (ImGui.Checkbox(_L("Set aside quests waiting on a duty you run yourself"), ref setAside))
        {
            Configuration.Duties.SetAsideManualDuties = setAside;
            Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            _L("When Questionable can't run a quest's duty, it opens the Duty Finder, sets the quest aside and carries on with other quests. Once you've run the duty, the quest comes back on its own."));

        using (ImRaii.Disabled(!_useAutoDuty))
        {
            bool catchUp = Configuration.Duties.CatchUpForStory;
            if (ImGui.Checkbox(_L("Catch up when the story is too high"), ref catchUp))
            {
                Configuration.Duties.CatchUpForStory = catchUp;
                Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                _L("When the next story quest needs a higher level, AutoDuty runs your best Duty Support dungeon until you get there. If your item level is short, Questionable first buys tomestone accessories (Knightshopper). Stops after 5 runs without an item level gain."));

            bool runUnsynced = Configuration.Duties.RunUnsynced;
            if (ImGui.Checkbox(_L("Run content unsynced where safe"), ref runUnsynced))
            {
                Configuration.Duties.RunUnsynced = runUnsynced;
                Save();
            }
            int levelGap = Math.Clamp(Configuration.Duties.UnsyncLevelGap, Duty.MinLevelGap, Duty.MaxLevelGap);
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                _LF("If your current job is at least {0} levels above a duty's sync level, or your average item level is at least 200 above " +
                    "its required item level, Questionable asks AutoDuty to run it solo as an Unrestricted Party. This includes trials.", levelGap));
            ImGui.SameLine();
            ImGui.TextColored(QstTheme.Danger, _L("Experimental feature"));

            using (ImRaii.Disabled(!runUnsynced))
            using (ImRaii.PushIndent())
            {
                ImGui.SetNextItemWidth(150f);
                if (ImGui.SliderInt(_L("Levels above a duty before it runs unsynced"), ref levelGap, Duty.MinLevelGap, Duty.MaxLevelGap))
                {
                    Configuration.Duties.UnsyncLevelGap = levelGap;
                    Save();
                }
            }
        }

        ImGui.Separator();

        using (ImRaii.Disabled(!_useAutoDuty))
        {
            ImGui.TextUnformatted(
                _L("Every duty with Duty Support runs by default. Set one to Disabled to do it yourself."));

            DrawConfigTable();

            DrawResetButton(Configuration.Duties.WhitelistedDutyCfcIds, Configuration.Duties.BlacklistedDutyCfcIds);
        }
    }

    private void DrawConfigTable()
    {
        using ImRaii.ChildDisposable child = ImRaii.Child("DutyConfiguration", new(ImGui.GetWindowContentRegionMax().X - 5, 300), border: true);
        if (!child)
            return;

        foreach (EExpansionVersion expansion in Enum.GetValues<EExpansionVersion>())
            DrawExpansionSection(expansion);
    }

    private void DrawExpansionSection(EExpansionVersion expansion)
    {
        (int enabledCount, int totalCount) = GetDutyCounts(expansion);
        if (totalCount == 0)
            return;

        string headerText = $"{expansion.ToFriendlyString()} ({enabledCount}/{totalCount})";

        string expansionKey = expansion.ToString();
        bool wasOpen = Configuration.Duties.ExpansionHeaderStates.GetValueOrDefault(expansionKey, defaultValue: false);

        ImGui.SetNextItemOpen(wasOpen, ImGuiCond.Always);
        bool isOpen = ImGui.CollapsingHeader(headerText);

        UpdateHeaderState(expansionKey, isOpen, wasOpen);

        if (!isOpen)
            return;

        if (!_dutiesByExpansion.TryGetValue(expansion, out List<DutyInfo>? duties))
            return;

        DrawDutyTable(expansion, duties);
    }

    private void UpdateHeaderState(string expansionKey, bool isOpen, bool wasOpen)
    {
        if (isOpen == wasOpen)
            return;

        Configuration.Duties.ExpansionHeaderStates[expansionKey] = isOpen;
        Save();
    }

    private void DrawDutyTable(EExpansionVersion expansion, List<DutyInfo> duties)
    {
        using ImRaii.TableDisposable table = ImRaii.Table($"Duties{expansion}", 2, ImGuiTableFlags.SizingFixedFit);
        if (!table)
            return;

        ImGui.TableSetupColumn(_L("Name"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(_L("Options"), ImGuiTableColumnFlags.WidthFixed, 200f);

        foreach (DutyInfo dutyInfo in duties)
        {
            if (!_questRegistry.TryGetDutyByCfcId(dutyInfo.CfcId, out DutyOptions? dutyOptions))
                continue;

            DrawDutyRow(dutyInfo, dutyOptions);
        }
    }

    private void DrawDutyRow(DutyInfo dutyInfo, DutyOptions dutyOptions)
    {
        ImGui.TableNextRow();

        if (ImGui.TableNextColumn())
            DrawDutyNameCell(dutyInfo, dutyOptions);

        if (ImGui.TableNextColumn())
            DrawDutyOptionsCell(dutyInfo.CfcId, dutyOptions);
    }

    private void DrawDutyNameCell(DutyInfo dutyInfo, DutyOptions dutyOptions)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(dutyInfo.Name);

        if (ImGui.IsItemHovered() && Configuration.Advanced.AdditionalStatusInformation)
            DrawDutyDebugTooltip(dutyInfo);

        if (_useAutoDuty && !_autoDuty.HasPath(dutyInfo.CfcId))
        {
            ImGuiComponents.HelpMarker(_L("This duty is not supported by AutoDuty."),
                FontAwesomeIcon.Times, QstTheme.Danger);
        }
        else if (dutyOptions.Notes.Count > 0)
            DrawNotes(dutyOptions.Enabled, dutyOptions.Notes);
    }

    private static void DrawDutyDebugTooltip(DutyInfo dutyInfo)
    {
        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
        ImGui.TextUnformatted(dutyInfo.Name);
        ImGui.Separator();
        ImGui.BulletText(_LF("TerritoryId: {0}", dutyInfo.TerritoryId));
        ImGui.BulletText(_LF("ContentFinderConditionId: {0}", dutyInfo.CfcId));
        ImGui.BulletText(_LF("ContentType: {0}", dutyInfo.ContentType.ToString()));
    }

    private void DrawDutyOptionsCell(uint cfcId, DutyOptions dutyOptions)
    {
        using ImRaii.IdDisposable _ = ImRaii.PushId($"##Dungeon{cfcId}");

        string[] labels = dutyOptions.Enabled ? SupportedCfcOptions : UnsupportedCfcOptions;
        int value = GetDutySelectionValue(cfcId);

        ImGui.SetNextItemWidth(200);
        if (!ImGui.Combo(string.Empty, ref value, labels, labels.Length))
            return;

        ApplyDutySelection(cfcId, value);
        Save();
    }

    private int GetDutySelectionValue(uint cfcId)
    {
        if (Configuration.Duties.WhitelistedDutyCfcIds.Contains(cfcId))
            return 1;
        if (Configuration.Duties.BlacklistedDutyCfcIds.Contains(cfcId))
            return 2;
        return 0;
    }

    private void ApplyDutySelection(uint cfcId, int value)
    {
        Configuration.Duties.WhitelistedDutyCfcIds.Remove(cfcId);
        Configuration.Duties.BlacklistedDutyCfcIds.Remove(cfcId);

        if (value == 1)
            Configuration.Duties.WhitelistedDutyCfcIds.Add(cfcId);
        else if (value == 2)
            Configuration.Duties.BlacklistedDutyCfcIds.Add(cfcId);
    }
    private (int enabledCount, int totalCount) GetDutyCounts(EExpansionVersion expansion)
    {
        if (!_dutiesByExpansion.TryGetValue(expansion, out List<DutyInfo>? duties))
            return (0, 0);

        int enabledCount = 0;
        int totalCount = 0;

        foreach ((uint cfcId, uint _, string _, EContentType _) in duties)
        {
            if (_questRegistry.TryGetDutyByCfcId(cfcId, out DutyOptions? dutyOptions))
            {
                totalCount++;

                bool isEnabled = Configuration.Duties.WhitelistedDutyCfcIds.Contains(cfcId) ||
                                 (!Configuration.Duties.BlacklistedDutyCfcIds.Contains(cfcId) && dutyOptions.Enabled);

                if (isEnabled)
                    enabledCount++;
            }
        }

        return (enabledCount, totalCount);
    }

    private sealed record DutyInfo(uint CfcId, uint TerritoryId, string Name, EContentType ContentType);
}
