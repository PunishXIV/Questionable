using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LLib.GameData;
using Lumina.Excel.Sheets;
using Questionable.Controller;
using Questionable.Data;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;

namespace Questionable.Windows.ConfigComponents;

internal sealed class GeneralConfigComponent : ConfigComponent
{
    private static readonly List<(uint Id, string Name)> DefaultMounts = [(0, "Mount Roulette")];
    private static readonly List<(EClassJob ClassJob, string Name)> DefaultClassJobs = [(EClassJob.Adventurer, "Auto (highest level/item level)")];

    private readonly QuestRegistry _questRegistry;
    private readonly TerritoryData _territoryData;

    private readonly uint[] _mountIds;
    private readonly string[] _mountNames;
    private readonly string[] _combatModuleNames = ["None", "Boss Mod (VBM)", "Wrath Combo", "Rotation Solver Reborn"];

    private readonly string[] _grandCompanyNames =
        ["None (manually pick quest)", "Maelstrom", "Twin Adder", "Immortal Flames"];

    private readonly EClassJob[] _classJobIds;
    private readonly string[] _classJobNames;

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

        var mounts = dataManager.GetExcelSheet<Mount>()
            .Where(x => x is { RowId: > 0, Icon: > 0 })
            .Select(x => (MountId: x.RowId, Name: x.Singular.ToString()))
            .Where(x => !string.IsNullOrEmpty(x.Name))
            .OrderBy(x => x.Name)
            .ToList();
        _mountIds = DefaultMounts.Select(x => x.Id).Concat(mounts.Select(x => x.MountId)).ToArray();
        _mountNames = DefaultMounts.Select(x => x.Name).Concat(mounts.Select(x => x.Name)).ToArray();

        var sortedClassJobs = classJobUtils.SortedClassJobs.Select(x => x.ClassJob).ToList();
        var classJobs = Enum.GetValues<EClassJob>()
            .Where(x => x != EClassJob.Adventurer)
            .Where(x => !x.IsCrafter() && !x.IsGatherer())
            .Where(x => !x.IsClass())
            .OrderBy(x => sortedClassJobs.IndexOf(x))
            .ToList();
        _classJobIds = DefaultClassJobs.Select(x => x.ClassJob).Concat(classJobs).ToArray();
        _classJobNames = DefaultClassJobs.Select(x => x.Name).Concat(classJobs.Select(x => x.ToFriendlyString())).ToArray();
    }

    public override void DrawTab()
    {
        using var tab = ImRaii.TabItem("General###General");
        if (!tab)
            return;


        {
            int selectedCombatModule = (int)Configuration.General.CombatModule;
            if (ImGui.Combo("Preferred Combat Module", ref selectedCombatModule, _combatModuleNames,
                    _combatModuleNames.Length))
            {
                Configuration.General.CombatModule = (Configuration.ECombatModule)selectedCombatModule;
                Save();
            }
        }

        int selectedMount = Array.FindIndex(_mountIds, x => x == Configuration.General.MountId);
        if (selectedMount == -1)
        {
            selectedMount = 0;
            Configuration.General.MountId = _mountIds[selectedMount];
            Save();
        }

        if (ImGui.Combo("Preferred Mount", ref selectedMount, _mountNames, _mountNames.Length))
        {
            Configuration.General.MountId = _mountIds[selectedMount];
            Save();
        }

        int grandCompany = (int)Configuration.General.GrandCompany;
        if (ImGui.Combo("Preferred Grand Company", ref grandCompany, _grandCompanyNames,
                _grandCompanyNames.Length))
        {
            Configuration.General.GrandCompany = (GrandCompany)grandCompany;
            Save();
        }

        int combatJob = Array.IndexOf(_classJobIds, Configuration.General.CombatJob);
        if (combatJob == -1)
        {
            Configuration.General.CombatJob = EClassJob.Adventurer;
            Save();

            combatJob = 0;
        }

        if (ImGui.Combo("Preferred Combat Job", ref combatJob, _classJobNames, _classJobNames.Length))
        {
            Configuration.General.CombatJob = _classJobIds[combatJob];
            Save();
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

            bool hideSeasonalFromJournal = Configuration.General.HideSeasonalEventsFromJournalProgress;
            if (ImGui.Checkbox("Hide Seasonal Events from Journal Progress", ref hideSeasonalFromJournal))
            {
                Configuration.General.HideSeasonalEventsFromJournalProgress = hideSeasonalFromJournal;
                Save();
            }
        }

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
                    foreach (var lowPriorityCfc in _questRegistry.LowPriorityContentFinderConditionQuests)
                    {
                        if (_territoryData.TryGetContentFinderCondition(lowPriorityCfc.ContentFinderConditionId, out var cfcData))
                        {
                            ImGui.BulletText($"{cfcData.Name}");
                        }
                    }
                }
            }

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
                if (ImGui.SliderInt("Refresh delay (seconds)", ref autoStepRefreshDelay, 10, 180))
                {
                    Configuration.General.AutoStepRefreshDelaySeconds = autoStepRefreshDelay;
                    Save();
                }

                ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                    $"Quest steps will refresh automatically after {autoStepRefreshDelay} seconds if no progress is made.");
                ImGui.Unindent();
            }
        }
    }
}