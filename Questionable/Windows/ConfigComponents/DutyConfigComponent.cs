using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using Questionable.Controller;
using Questionable.Data;
using Questionable.External;
using Questionable.Model;
using Questionable.Model.Questing;
namespace Questionable.Windows.ConfigComponents;

internal sealed class DutyConfigComponent : ConfigComponent
{
    private const string DutyClipboardPrefix = "qst:duty:";
    private readonly AutoDutyIpc _autoDutyIpc;
    private readonly Dictionary<EExpansionVersion, List<DutyInfo>> _contentFinderConditionNames;

    private readonly QuestRegistry _questRegistry;

    public DutyConfigComponent(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        IDataManager dataManager,
        QuestRegistry questRegistry,
        AutoDutyIpc autoDutyIpc,
        TerritoryData territoryData)
        : base(pluginInterface, configuration)
    {
        _questRegistry = questRegistry;
        _autoDutyIpc = autoDutyIpc;

        _contentFinderConditionNames = dataManager.GetExcelSheet<DawnContent>()
            .Where(x => x is { RowId: > 0, Unknown16: false })
            .OrderBy(x => x.Unknown15) // SortKey for the support UI
            .Select(x => x.Content.ValueNullable)
            .Where(x => x != null)
            .Select(x => x!.Value)
            .Select(x => new
            {
                Expansion = (EExpansionVersion)x.TerritoryType.Value.ExVersion.RowId,
                CfcId = x.RowId,
                Name = territoryData.GetContentFinderCondition(x.RowId)?.Name ?? "?",
                TerritoryId = x.TerritoryType.RowId,
                ContentType = x.ContentType.RowId,
                Level = x.ClassJobLevelRequired,
                x.SortKey
            })
            .GroupBy(x => x.Expansion)
            .ToDictionary(x => x.Key,
                x => x
                    .Select(y => new DutyInfo(y.CfcId, y.TerritoryId, $"{FormatLevel(y.Level)} {y.Name}"))
                    .ToList());
    }

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem("Duties###Duties");
        if (!tab)
            return;

        bool runInstancedContentWithAutoDuty = Configuration.Duties.RunInstancedContentWithAutoDuty;
        if (ImGui.Checkbox("Run instanced content with AutoDuty and BossMod", ref runInstancedContentWithAutoDuty))
        {
            Configuration.Duties.RunInstancedContentWithAutoDuty = runInstancedContentWithAutoDuty;
            Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "The combat module used for this is configured by AutoDuty, ignoring whichever selection you've made in Questionable's \"General\" configuration.");

        ImGui.Separator();

        using (ImRaii.Disabled(!runInstancedContentWithAutoDuty))
        {
            ImGui.Text(
                "Questionable includes a default list of duties that work if AutoDuty and BossMod are installed.");

            ImGui.Text(
                "The included list of duties can change with each update, and is based on the following spreadsheet:");
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.GlobeEurope, "Open AutoDuty spreadsheet"))
            {
                Util.OpenLink(
                    "https://docs.google.com/spreadsheets/d/151RlpqRcCpiD_VbQn6Duf-u-S71EP7d0mx3j1PDNoNA/edit?pli=1#gid=0");
            }

            ImGui.Separator();
            ImGui.Text("You can override the settings for each individual dungeon/trial:");

            DrawConfigTable(runInstancedContentWithAutoDuty);

            DrawEnableAllButton();
            ImGui.SameLine();
            DrawClipboardButtons();
            ImGui.SameLine();
            DrawResetButton();
        }
    }

    private void DrawConfigTable(bool runInstancedContentWithAutoDuty)
    {
        using ImRaii.ChildDisposable child = ImRaii.Child("DutyConfiguration", new(650, 400), true);
        if (!child)
            return;

        foreach (EExpansionVersion expansion in Enum.GetValues<EExpansionVersion>())
        {
            (int enabledCount, int totalCount) = GetDutyCountsForExpansion(expansion);

            string headerText = totalCount > 0
                ? $"{expansion.ToFriendlyString()} ({enabledCount}/{totalCount})"
                : expansion.ToFriendlyString();

            string expansionKey = expansion.ToString();

            bool isHeaderOpen = Configuration.Duties.ExpansionHeaderStates.GetValueOrDefault(expansionKey, false);

            ImGui.SetNextItemOpen(isHeaderOpen, ImGuiCond.Always);

            if (ImGui.CollapsingHeader(headerText))
            {
                if (!Configuration.Duties.ExpansionHeaderStates.GetValueOrDefault(expansionKey, false))
                {
                    Configuration.Duties.ExpansionHeaderStates[expansionKey] = true;
                    Save();
                }

                using ImRaii.TableDisposable table = ImRaii.Table($"Duties{expansion}", 2, ImGuiTableFlags.SizingFixedFit);
                if (table)
                {
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed, 200f);

                    if (_contentFinderConditionNames.TryGetValue(expansion, out List<DutyInfo>? cfcNames))
                    {
                        foreach ((uint cfcId, uint territoryId, string name) in cfcNames)
                        {
                            if (_questRegistry.TryGetDutyByContentFinderConditionId(cfcId, out DutyOptions? dutyOptions))
                            {
                                ImGui.TableNextRow();

                                string[] labels = dutyOptions.Enabled
                                    ? SupportedCfcOptions
                                    : UnsupportedCfcOptions;
                                int value = 0;
                                if (Configuration.Duties.WhitelistedDutyCfcIds.Contains(cfcId))
                                    value = 1;
                                if (Configuration.Duties.BlacklistedDutyCfcIds.Contains(cfcId))
                                    value = 2;

                                if (ImGui.TableNextColumn())
                                {
                                    ImGui.AlignTextToFramePadding();
                                    ImGui.TextUnformatted(name);
                                    if (ImGui.IsItemHovered() &&
                                        Configuration.Advanced.AdditionalStatusInformation)
                                    {
                                        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
                                        ImGui.TextUnformatted(name);
                                        ImGui.Separator();
                                        ImGui.BulletText($"TerritoryId: {territoryId}");
                                        ImGui.BulletText($"ContentFinderConditionId: {cfcId}");
                                    }

                                    if (runInstancedContentWithAutoDuty && !_autoDutyIpc.HasPath(cfcId))
                                    {
                                        ImGuiComponents.HelpMarker("This duty is not supported by AutoDuty",
                                            FontAwesomeIcon.Times, ImGuiColors.DalamudRed);
                                    }
                                    else if (dutyOptions.Notes.Count > 0)
                                        DrawNotes(dutyOptions.Enabled, dutyOptions.Notes);
                                }

                                if (ImGui.TableNextColumn())
                                {
                                    using ImRaii.IdDisposable _ = ImRaii.PushId($"##Dungeon{cfcId}");
                                    ImGui.SetNextItemWidth(200);
                                    if (ImGui.Combo(string.Empty, ref value, labels, labels.Length))
                                    {
                                        Configuration.Duties.WhitelistedDutyCfcIds.Remove(cfcId);
                                        Configuration.Duties.BlacklistedDutyCfcIds.Remove(cfcId);

                                        if (value == 1)
                                            Configuration.Duties.WhitelistedDutyCfcIds.Add(cfcId);
                                        else if (value == 2)
                                            Configuration.Duties.BlacklistedDutyCfcIds.Add(cfcId);

                                        Save();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (Configuration.Duties.ExpansionHeaderStates.GetValueOrDefault(expansionKey, false))
                {
                    Configuration.Duties.ExpansionHeaderStates[expansionKey] = false;
                    Save();
                }
            }
        }
    }
    private (int enabledCount, int totalCount) GetDutyCountsForExpansion(EExpansionVersion expansion)
    {
        if (!_contentFinderConditionNames.TryGetValue(expansion, out List<DutyInfo>? cfcNames))
            return (0, 0);

        int enabledCount = 0;
        int totalCount = 0;

        foreach ((uint cfcId, uint _, string _) in cfcNames)
        {
            if (_questRegistry.TryGetDutyByContentFinderConditionId(cfcId, out DutyOptions? dutyOptions))
            {
                totalCount++;

                // a duty is considered "enabled" if:
                // it's whitelisted, OR
                // it's not blacklisted AND it's enabled by default
                bool isEnabled = Configuration.Duties.WhitelistedDutyCfcIds.Contains(cfcId) ||
                                 (!Configuration.Duties.BlacklistedDutyCfcIds.Contains(cfcId) && dutyOptions.Enabled);

                if (isEnabled)
                    enabledCount++;
            }
        }

        return (enabledCount, totalCount);
    }

    private void DrawEnableAllButton()
    {
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.CheckCircle, "Enable All"))
        {
            Configuration.Duties.BlacklistedDutyCfcIds.Clear();
            Configuration.Duties.WhitelistedDutyCfcIds.Clear();

            foreach (List<DutyInfo> cfcNames in _contentFinderConditionNames.Values)
            {
                foreach ((uint cfcId, uint _, string _) in cfcNames)
                {
                    if (_questRegistry.TryGetDutyByContentFinderConditionId(cfcId, out DutyOptions? _))
                        Configuration.Duties.WhitelistedDutyCfcIds.Add(cfcId);
                }
            }

            Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Enable all of the duties, use at your own risk.");
    }

    private void DrawClipboardButtons()
    {
        using (ImRaii.Disabled(Configuration.Duties.WhitelistedDutyCfcIds.Count +
            Configuration.Duties.BlacklistedDutyCfcIds.Count == 0))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, "Export to clipboard"))
            {
                IEnumerable<string> whitelisted =
                    Configuration.Duties.WhitelistedDutyCfcIds.Select(x => $"{DutyWhitelistPrefix}{x}");
                IEnumerable<string> blacklisted =
                    Configuration.Duties.BlacklistedDutyCfcIds.Select(x => $"{DutyBlacklistPrefix}{x}");
                string text = DutyClipboardPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    string.Join(DutyClipboardSeparator, whitelisted.Concat(blacklisted))));
                ImGui.SetClipboardText(text);
            }
        }

        ImGui.SameLine();

        string clipboardText = ImGui.GetClipboardText().Trim();
        using (ImRaii.Disabled(string.IsNullOrEmpty(clipboardText) ||
                               !clipboardText.StartsWith(DutyClipboardPrefix, StringComparison.InvariantCulture)))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Paste, "Import from Clipboard"))
            {
                clipboardText = clipboardText.Substring(DutyClipboardPrefix.Length);
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(clipboardText));

                Configuration.Duties.WhitelistedDutyCfcIds.Clear();
                Configuration.Duties.BlacklistedDutyCfcIds.Clear();
                foreach (string part in text.Split(DutyClipboardSeparator))
                {
                    if (part.StartsWith(DutyWhitelistPrefix, StringComparison.InvariantCulture) &&
                        uint.TryParse(part.AsSpan(DutyWhitelistPrefix.Length), CultureInfo.InvariantCulture,
                            out uint whitelistedCfcId))
                    {
                        Configuration.Duties.WhitelistedDutyCfcIds.Add(whitelistedCfcId);
                    }

                    if (part.StartsWith(DutyBlacklistPrefix, StringComparison.InvariantCulture) &&
                        uint.TryParse(part.AsSpan(DutyBlacklistPrefix.Length), CultureInfo.InvariantCulture,
                            out uint blacklistedCfcId))
                    {
                        Configuration.Duties.BlacklistedDutyCfcIds.Add(blacklistedCfcId);
                    }
                }
            }
        }
    }

    private void DrawResetButton()
    {
        using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Undo, "Reset to default"))
            {
                Configuration.Duties.WhitelistedDutyCfcIds.Clear();
                Configuration.Duties.BlacklistedDutyCfcIds.Clear();
                Save();
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold CTRL to enable this button.");
    }

    private sealed record DutyInfo(uint CfcId, uint TerritoryId, string Name);
}
