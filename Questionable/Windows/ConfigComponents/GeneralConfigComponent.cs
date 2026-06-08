using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using Lumina.Excel.Sheets;
using Questionable.Controller;
using Questionable.Data;
using Questionable.External;
using Questionable.Model.Questing;
using Questionable.Utils;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;

namespace Questionable.Windows.ConfigComponents;

internal sealed class GeneralConfigComponent : ConfigComponent
{
    private static readonly (uint Id, string Name) DefaultMount = (0, "Mount Roulette");
    private static readonly (Job ClassJob, string Name) DefaultClassJob = (Job.ADV, "Auto (highest level/item level)");

    private readonly string[] _grandCompanyNames =
        ["None (manually pick quest)", "Maelstrom", "Twin Adder", "Immortal Flames"];

    private readonly QuestRegistry _questRegistry;
    private readonly TerritoryData _territoryData;
    private readonly Lazy<List<Job>> _sortedClassJobs;
    private readonly Lazy<(uint[] Ids, string[] Names)> _mounts;
    private readonly Lazy<(Job[] Ids, string[] Names)> _classJobs;
    private readonly Lazy<(Job[] Ids, string[] Names)> _craftJobs;
    private readonly Lazy<(Job[] Ids, string[] Names)> _gatherJobs;
    private string _mountSearchString = string.Empty;

    public GeneralConfigComponent(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        IDataManager dataManager,
        ClassJobUtils classJobUtils,
        QuestRegistry questRegistry,
        TerritoryData territoryData)
        : base(pluginInterface, configuration)
    {
        _questRegistry = questRegistry;
        _territoryData = territoryData;

        _sortedClassJobs = new(() => [.. classJobUtils.SortedClassJobs.Select(x => x.ClassJob)]);
        _mounts = new(() => BuildMounts(dataManager));
        _classJobs = new(() => BuildJobList(
            Enum.GetValues<Job>().Where(x => x != Job.ADV && !x.IsCrafter() && !x.IsGatherer() && !x.IsClass()),
            prependDefault: true));
        _craftJobs = new(() => BuildJobList(
            Enum.GetValues<Job>().Where(x => x != Job.ADV && x.IsCrafter()),
            prependDefault: false));
        _gatherJobs = new(() => BuildJobList(
            Enum.GetValues<Job>().Where(x => x == Job.MIN || x == Job.BTN),
            prependDefault: false));
    }

    private static (uint[] Ids, string[] Names) BuildMounts(IDataManager dataManager)
    {
        List<(uint MountId, string Name)> mounts = dataManager.GetExcelSheet<Mount>()
            .Where(x => x is { RowId: > 0, Icon: > 0 })
            .Select(x => (MountId: x.RowId, Name: x.Singular.ToString()))
            .Where(x => !string.IsNullOrEmpty(x.Name))
            .OrderBy(x => x.Name)
            .ToList();
        uint[] ids = [DefaultMount.Id, .. mounts.Select(x => x.MountId)];
        string[] names = [DefaultMount.Name, .. mounts.Select(x => x.Name)];
        return (ids, names);
    }

    private (Job[] Ids, string[] Names) BuildJobList(IEnumerable<Job> source, bool prependDefault)
    {
        List<Job> sorted = _sortedClassJobs.Value;
        List<Job> jobs = [.. source.OrderBy(x => sorted.IndexOf(x))];
        if (prependDefault)
        {
            Job[] ids = [DefaultClassJob.ClassJob, .. jobs];
            string[] names = [DefaultClassJob.Name, .. jobs.Select(x => x.ToString())];
            return (ids, names);
        }
        else
        {
            return ([.. jobs], [.. jobs.Select(x => x.ToString())]);
        }
    }

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem("General###General");
        if (!tab)
            return;


        Configuration.ECombatModule combatModule = Configuration.General.CombatModule;
        if (ImGuiEx.EnumCombo("Preferred Combat Module", ref combatModule))
        {
            Configuration.General.CombatModule = combatModule;
            Save();
        }

        (uint[] mountIds, string[] mountNames) = _mounts.Value;
        uint mountId = Configuration.General.MountId;
        if (ImGuiComponentsLocal.DrawSearchableCombo("Preferred Mount", mountIds, mountNames,
            Configuration.General.MountId, ref _mountSearchString, ref mountId))
        {
            Configuration.General.MountId = mountId;
            Save();
        }

        int grandCompany = (int)Configuration.General.GrandCompany;
        if (ImGui.Combo("Preferred Grand Company", ref grandCompany, _grandCompanyNames,
            _grandCompanyNames.Length))
        {
            Configuration.General.GrandCompany = (GrandCompany)grandCompany;
            Save();
        }

        (Job[] classJobIds, string[] classJobNames) = _classJobs.Value;
        DrawComboOption("Preferred Combat Job", classJobIds, classJobNames,
            () => Configuration.General.CombatJob,
            v => Configuration.General.CombatJob = v);

        (Job[] craftJobIds, string[] craftJobNames) = _craftJobs.Value;
        DrawComboOption("Preferred Crafting Job", craftJobIds, craftJobNames,
            () => Configuration.General.CraftingJob,
            v => Configuration.General.CraftingJob = v);

        (Job[] gatherJobIds, string[] gatherJobNames) = _gatherJobs.Value;
        DrawComboOption("Preferred Gathering Job", gatherJobIds, gatherJobNames,
            () => Configuration.General.GatheringJob,
            v => Configuration.General.GatheringJob = v);

        using (ImRaii.Disabled(!StylistIpc.IsInstalled))
        {
            Configuration.EGearsetUpdateSource gearsetSource = Configuration.General.GearsetUpdateSource;
            if (ImGuiEx.EnumCombo("Preferred Gear Upgrade Source", ref gearsetSource))
            {
                Configuration.General.GearsetUpdateSource = gearsetSource;
                Save();
            }
            if (!StylistIpc.IsInstalled && gearsetSource is Configuration.EGearsetUpdateSource.Stylist)
            {
                Svc.Chat.Print("You've set Stylist to manage equipped gear, but it is not installed. Resetting to Vanilla.", CommandHandler.MessageTag, CommandHandler.TagColor);
                Configuration.General.GearsetUpdateSource = Configuration.EGearsetUpdateSource.Vanilla;
                Save();
            }
        }

        string chocoboName = Configuration.General.ChocoboName;
        if (ImGui.InputText("Chocobo name", ref chocoboName, 20))
            Configuration.General.ChocoboName = chocoboName;

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            if (string.IsNullOrWhiteSpace(Configuration.General.ChocoboName))
                Configuration.General.ChocoboName = "Chicken";
            Save();
        }

        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.Text("The name to give your chocobo during the \"My Little Chocobo\" quest.");
                ImGui.Text("Defaults to \"Chicken\" if left blank.");
            }
        }

        string displayName = Configuration.General.DisplayName;
        if (ImGui.InputText("Display name", ref displayName, 20))
            Configuration.General.DisplayName = displayName;

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            if (string.IsNullOrWhiteSpace(Configuration.General.DisplayName))
                Configuration.General.DisplayName = "Anonymous";
            Save();
        }

        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.Text("The name associated with submissions to help with QST's development.");
                ImGui.Text("Defaults to \"Anonymous\" if left blank.");
            }
        }

        ImGui.Separator();
        ImGui.Text("UI");
        using (ImRaii.PushIndent())
        {
            bool hideInAllInstances = Configuration.General.HideInAllInstances;
            if (ImGui.Checkbox("Hide quest window in all instanced duties", ref hideInAllInstances))
            {
                Configuration.General.HideInAllInstances = hideInAllInstances;
                Save();
            }

            bool useEscToCancelQuesting = Configuration.General.UseEscToCancelQuesting;
            if (ImGui.Checkbox("Use ESC to cancel questing/movement", ref useEscToCancelQuesting))
            {
                Configuration.General.UseEscToCancelQuesting = useEscToCancelQuesting;
                Save();
            }

            bool showIncompleteSeasonalEvents = Configuration.General.ShowIncompleteSeasonalEvents;
            if (ImGui.Checkbox("Show details for incomplete seasonal events", ref showIncompleteSeasonalEvents))
            {
                Configuration.General.ShowIncompleteSeasonalEvents = showIncompleteSeasonalEvents;
                Save();
            }

            bool hideSponsorButton = Configuration.General.HideSponsorButton;
            if (ImGui.Checkbox("Hide Sponsor button", ref hideSponsorButton))
            {
                Configuration.General.HideSponsorButton = hideSponsorButton;
                Save();
            }
        }

#if REPORTING
        ImGui.Separator();
        ImGui.Text("Bug Report");
        using (ImRaii.PushIndent())
        {
            bool reportOptOut = Configuration.General.ReportsDisabled;
            if (ImGui.Checkbox("Opt out of bug reports", ref reportOptOut))
            {
                Configuration.General.ReportsDisabled = reportOptOut;
                Configuration.General.DismissedReportWarning = true;
                Save();
            }

            bool dismissedReportWarning = Configuration.General.DismissedReportWarning;
            if (ImGui.Checkbox("Hide Report warning", ref dismissedReportWarning))
            {
                Configuration.General.DismissedReportWarning = dismissedReportWarning;
                Save();
            }

            if (!reportOptOut)
            {
                string reportMessage = Configuration.General.ReportMessage;
                if (ImGui.InputText("Report message", ref reportMessage, 256))
                {
                    Configuration.General.ReportMessage = reportMessage;
                    Save();
                }
            }
        }
#endif

        ImGui.Separator();
        ImGui.Text("Questing");
        using (ImRaii.PushIndent())
        {
            bool configureTextAdvance = Configuration.General.ConfigureTextAdvance;
            if (ImGui.Checkbox("Automatically configure TextAdvance with the recommended settings",
                ref configureTextAdvance))
            {
                Configuration.General.ConfigureTextAdvance = configureTextAdvance;
                Save();
            }

            if (configureTextAdvance)
            {
                bool dontSkipCutscenes = Configuration.General.DontSkipCutscenes;
                using (ImRaii.PushIndent())
                {
                    if (ImGui.Checkbox("but don't skip cutscenes or dialogue", ref dontSkipCutscenes))
                    {
                        Configuration.General.DontSkipCutscenes = dontSkipCutscenes;
                        Save();
                    }
                }
                if (dontSkipCutscenes)
                {
                    using (ImRaii.PushIndent(2))
                    {
                        bool dontShowAnswerSuggestions = Configuration.General.DontShowAnswerSuggestions;
                        if (ImGui.Checkbox("and don't show which answer we would have picked for you", ref dontShowAnswerSuggestions))
                        {
                            Configuration.General.DontShowAnswerSuggestions = dontShowAnswerSuggestions;
                            Save();
                        }
                    }
                }
            }

            bool skipLowPriorityInstances = Configuration.General.SkipLowPriorityDuties;
            if (ImGui.Checkbox("Unlock certain optional dungeons and raids (instead of waiting for completion)", ref skipLowPriorityInstances))
            {
                Configuration.General.SkipLowPriorityDuties = skipLowPriorityInstances;
                Save();
            }

            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextDisabled(FontAwesomeIcon.InfoCircle.ToIconString());
            }

            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text("Questionable automatically picks up some optional quests (e.g. for aether currents, or the ARR alliance raids).");
                    ImGui.Text("If this setting is enabled, Questionable will continue with other quests, instead of waiting for manual completion of the duty.");

                    ImGui.Separator();
                    ImGui.Text("This affects the following dungeons and raids:");
                    foreach ((uint ContentFinderConditionId, ElementId QuestId, int Sequence) lowPriorityCfc in _questRegistry.LowPriorityContentFinderConditionQuests)
                    {
                        if (_territoryData.TryGetContentFinderCondition(lowPriorityCfc.ContentFinderConditionId, out TerritoryData.ContentFinderConditionData? cfcData))
                            ImGui.BulletText($"{cfcData.Name}");
                    }
                }
            }

            bool useTickets = Configuration.General.UseTickets;
            if (ImGui.Checkbox("Use aetheryte tickets where available", ref useTickets))
            {
                Configuration.General.UseTickets = useTickets;
                Save();
            }

            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text("Ideally this should be set in the in-game Teleport settings, but is provided here for convenience.");
                }
            }

#if false
            ImGui.Spacing();
            bool autoStepRefreshEnabled = Configuration.General.AutoStepRefreshEnabled;
            if (ImGui.Checkbox("Automatically refresh quest steps when stuck (WIP see tooltip)", ref autoStepRefreshEnabled))
            {
                Configuration.General.AutoStepRefreshEnabled = autoStepRefreshEnabled;
                Save();
            }

            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextDisabled(FontAwesomeIcon.InfoCircle.ToIconString());
            }

            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text("Questionable will automatically refresh a quest step if it appears to be stuck after the configured delay.");
                    ImGui.Text("This helps resume automated quest completion when interruptions occur.");
                    ImGui.Text("WIP feature, rather than remove it, this is a warning that it isn't fully complete.");
                }
            }

            using (ImRaii.Disabled(!autoStepRefreshEnabled))
            {
                ImGui.Indent();
                int autoStepRefreshDelay = Configuration.General.AutoStepRefreshDelaySeconds;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.SliderInt("Refresh delay (seconds)", ref autoStepRefreshDelay, 30, 180))
                {
                    Configuration.General.AutoStepRefreshDelaySeconds = autoStepRefreshDelay;
                    Save();
                }

                ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                    $"Quest steps will refresh automatically after {autoStepRefreshDelay} seconds if no progress is made.");
                ImGui.Unindent();
            }
#endif
        }
    }
}
