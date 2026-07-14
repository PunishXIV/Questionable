using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
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
using ECommons.ExcelServices;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Utils;
using static Questionable.Utils.LocalizeShortcut;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Windows.ConfigComponents;

// TODO: refactor — heavy nesting (39 lines indented ≥6 levels, max indent ~8 levels). Likely mirrors DutyConfigComponent structure.
internal sealed class SinglePlayerDutyConfigComponent : ConfigComponent
{
    private const string SinglePlayerDutyClipboardPrefix = "qst:single:";

    private static readonly List<(Job ClassJob, string Name)> RoleQuestCategories =
    [
        (Job.PLD, _L("Tank Role Quests")),
        (Job.WHM, _L("Healer Role Quests")),
        (Job.LNC, _L("Melee Role Quests")),
        (Job.BRD, _L("Physical Ranged Role Quests")),
        (Job.BLM, _L("Magical Ranged Role Quests"))
    ];

#if false
    private readonly string[] _retryDifficulties = [_L("Normal"), _L("Easy"), _L("Very Easy")];
#endif

    private readonly TerritoryData _territoryData;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestData _questData;
    private readonly IDataManager _dataManager;
    private readonly ClassJobUtils _classJobUtils;
    private readonly ILogger<SinglePlayerDutyConfigComponent> _logger;

    private ImmutableDictionary<EAetheryteLocation, List<SinglePlayerDutyInfo>> _startingCityBattles =
        ImmutableDictionary<EAetheryteLocation, List<SinglePlayerDutyInfo>>.Empty;

    private ImmutableDictionary<EExpansionVersion, List<SinglePlayerDutyInfo>> _mainScenarioBattles =
        ImmutableDictionary<EExpansionVersion, List<SinglePlayerDutyInfo>>.Empty;

    private ImmutableDictionary<Job, List<SinglePlayerDutyInfo>> _jobQuestBattles =
        ImmutableDictionary<Job, List<SinglePlayerDutyInfo>>.Empty;

    private ImmutableDictionary<Job, List<SinglePlayerDutyInfo>> _roleQuestBattles =
        ImmutableDictionary<Job, List<SinglePlayerDutyInfo>>.Empty;

    private ImmutableList<SinglePlayerDutyInfo> _otherRoleQuestBattles = ImmutableList<SinglePlayerDutyInfo>.Empty;

    private ImmutableList<(string Label, List<SinglePlayerDutyInfo>)> _otherQuestBattles =
        ImmutableList<(string Label, List<SinglePlayerDutyInfo>)>.Empty;

    public SinglePlayerDutyConfigComponent(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        TerritoryData territoryData,
        QuestRegistry questRegistry,
        QuestData questData,
        IDataManager dataManager,
        ClassJobUtils classJobUtils,
        ILogger<SinglePlayerDutyConfigComponent> logger) : base(pluginInterface, configuration)
    {
        _territoryData = territoryData;
        _questRegistry = questRegistry;
        _questData = questData;
        _dataManager = dataManager;
        _classJobUtils = classJobUtils;
        _logger = logger;
        _questRegistry.Reloaded += Reload;
    }

    public void Dispose()
    {
        _questRegistry.Reloaded -= Reload;
    }

    public void Reload(object? sender = null, EventArgs? e = null)
    {
        List<ElementId> questsWithMultipleBattles = _territoryData.GetAllQuestsWithQuestBattles()
            .GroupBy(x => x.QuestId)
            .Where(x => x.Skip(1).Any())
            .Select(x => x.Key)
            .ToList();

        List<SinglePlayerDutyInfo> mainScenarioBattles = [];
        Dictionary<EAetheryteLocation, List<SinglePlayerDutyInfo>> startingCityBattles =
            new()
            {
                { EAetheryteLocation.Limsa, [] },
                { EAetheryteLocation.Gridania, [] },
                { EAetheryteLocation.Uldah, [] }
            };

        List<SinglePlayerDutyInfo> otherBattles = [];

        Dictionary<ElementId, Job> questIdsToJob = Enum.GetValues<Job>()
            .Where(x => x != Job.ADV && !x.IsCrafter() && !x.IsGatherer() && (x.IsClass() || !x.HasBaseClass()))
            .SelectMany(x => _questRegistry.GetKnownClassJobQuests(x, includeRoleQuests: false)
            .Select(y => (y.QuestId, ClassJob: x)))
            .ToDictionary(x => x.QuestId, x => x.ClassJob);
        Dictionary<Job, List<SinglePlayerDutyInfo>> jobQuestBattles = questIdsToJob.Values.Distinct()
            .ToDictionary(x => x, _ => new List<SinglePlayerDutyInfo>());

        Dictionary<ElementId, List<Job>> questIdToRole = RoleQuestCategories
            .SelectMany(x => _questData.GetRoleQuests(x.ClassJob).Select(y => (y.QuestId, x.ClassJob)))
            .GroupBy(x => x.QuestId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.ClassJob).ToList());
        Dictionary<Job, List<SinglePlayerDutyInfo>> roleQuestBattles = RoleQuestCategories
            .ToDictionary(x => x.ClassJob, _ => new List<SinglePlayerDutyInfo>());
        List<SinglePlayerDutyInfo> otherRoleQuestBattles = [];

        foreach ((ElementId questId, byte index, TerritoryData.ContentFinderConditionData cfcData) in _territoryData.GetAllQuestsWithQuestBattles())
        {
            IQuestInfo questInfo = _questData.GetQuestInfo(questId);
            (bool enabled, SinglePlayerDutyOptions options) = FindDutyOptions(questId, index);

            string name = $"{FormatLevel(questInfo.Level)} {questInfo.Name}";
            if (!string.IsNullOrEmpty(cfcData.Name) && !questInfo.Name.EndsWith(cfcData.Name, StringComparison.Ordinal))
                name += _LF(" ({0})", cfcData.Name);

            if (questsWithMultipleBattles.Contains(questId))
                name += _LF(" (Part {0})", options.Index + 1);
            else if (cfcData.ContentFinderConditionId is 674 or 691)
                name += _L(" (Melee/Phys. Ranged)");

            SinglePlayerDutyInfo dutyInfo = new(name, questInfo, cfcData, options, enabled);

            if (dutyInfo.IsLimsaStart)
                startingCityBattles[EAetheryteLocation.Limsa].Add(dutyInfo);
            else if (dutyInfo.IsGridaniaStart)
                startingCityBattles[EAetheryteLocation.Gridania].Add(dutyInfo);
            else if (dutyInfo.IsUldahStart)
                startingCityBattles[EAetheryteLocation.Uldah].Add(dutyInfo);
            else if (questInfo.IsMainScenarioQuest)
                mainScenarioBattles.Add(dutyInfo);
            else if (questIdsToJob.TryGetValue(questId, out Job classJob))
                jobQuestBattles[classJob].Add(dutyInfo);
            else if (questIdToRole.TryGetValue(questId, out List<Job>? classJobs))
            {
                foreach (Job roleClassJob in classJobs)
                    roleQuestBattles[roleClassJob].Add(dutyInfo);
            }
            else if (dutyInfo.IsOtherRoleQuest)
                otherRoleQuestBattles.Add(dutyInfo);
            else
                otherBattles.Add(dutyInfo);
        }

        _startingCityBattles = startingCityBattles
            .ToImmutableDictionary(x => x.Key,
                x => x.Value.OrderBy(y => y.SortKey)
                    .ToList());
        _mainScenarioBattles = mainScenarioBattles
            .GroupBy(x => x.Expansion)
            .ToImmutableDictionary(x => x.Key,
                x =>
                    x.OrderBy(y => y.JournalGenreId)
                        .ThenBy(y => y.SortKey)
                        .ThenBy(y => y.Index)
                        .ToList());
        _jobQuestBattles = jobQuestBattles
            .Where(x => x.Value.Count > 0)
            .ToImmutableDictionary(x => x.Key,
                x =>
                    x.Value
                        // level 10 quests use the same quest battle for [you started as this class] and [you picked this class up later]
                        .DistinctBy(y => y.ContentFinderConditionId)
                        .OrderBy(y => y.JournalGenreId)
                        .ThenBy(y => y.SortKey)
                        .ThenBy(y => y.Index)
                        .ToList());
        _roleQuestBattles = roleQuestBattles
            .ToImmutableDictionary(x => x.Key,
                x =>
                    x.Value.OrderBy(y => y.JournalGenreId)
                        .ThenBy(y => y.SortKey)
                        .ThenBy(y => y.Index)
                        .ToList());
        _otherRoleQuestBattles = otherRoleQuestBattles.ToImmutableList();
        _otherQuestBattles = otherBattles
            .OrderBy(x => x.JournalGenreId)
            .ThenBy(x => x.SortKey)
            .ThenBy(x => x.Index)
            .GroupBy(x => x.JournalGenreId)
            .Select(x => (BuildJournalGenreLabel(x.Key), x.ToList()))
            .ToImmutableList();
    }

    private (bool Enabled, SinglePlayerDutyOptions Options) FindDutyOptions(ElementId questId, byte index)
    {
        SinglePlayerDutyOptions options = new()
        {
            Index = 0,
            Enabled = false
        };
        if (_questRegistry.TryGetQuest(questId, out Quest? quest))
        {
            if (quest.Root.Disabled)
            {
                _logger.LogDebug("Disabling quest battle for quest {QuestId}, quest is disabled", questId);
                return (false, options);
            }

            QuestStep? foundStep = quest.AllSteps()
                .Select(x => x.Step)
                .FirstOrDefault(x =>
                    x.InteractionType == EInteractionType.SinglePlayerDuty &&
                    x.SinglePlayerDutyIndex == index);
            if (foundStep == null)
            {
                _logger.LogWarning(
                    "Disabling quest battle for quest {QuestId}, no battle with index {Index} found", questId,
                    index);
                return (false, options);
            }

            return (true, foundStep.SinglePlayerDutyOptions ?? options);
        }

        _logger.LogDebug("Disabling quest battle for quest {QuestId}, unknown quest", questId);
        return (false, options);
    }

    private string BuildJournalGenreLabel(uint journalGenreId)
    {
        JournalGenre journalGenre = _dataManager.GetExcelSheet<JournalGenre>().GetRow(journalGenreId);
        JournalCategory journalCategory = journalGenre.JournalCategory.Value;

        string genreName = journalGenre.Name.ExtractText();
        string categoryName = journalCategory.Name.ExtractText();

        return _LF("{0} \u203B {1}", categoryName, genreName);
    }

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Quest Battles") + "###QuestBattles");
        if (!tab)
            return;

        bool runSoloInstancesWithBossMod = Configuration.SinglePlayerDuties.RunSoloInstancesWithBossMod;
        if (ImGui.Checkbox(_L("Run quest battles with BossMod"), ref runSoloInstancesWithBossMod))
        {
            Configuration.SinglePlayerDuties.RunSoloInstancesWithBossMod = runSoloInstancesWithBossMod;
            Save();
        }

        using (ImRaii.PushIndent(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X))
        {
            using (_ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
            {
                ImGui.TextUnformatted(_L("Work in Progress:"));
                ImGui.BulletText(_L("Will always use BossMod for combat (ignoring the configured combat module)."));
                ImGui.BulletText(_L("Only a small subset of quest battles have been tested - most of which are in the MSQ."));
                ImGui.BulletText(_L("When retrying a failed battle, it will always start at 'Very Easy' difficulty."));
                ImGui.BulletText(_L("Please don't enable this option when using a BossMod fork (such as Reborn);\nwith the missing combat module configuration, it is unlikely to be compatible."));
            }

#if false
            using (ImRaii.Disabled(!runSoloInstancesWithBossMod))
            {
                ImGui.Spacing();
                int retryDifficulty = Configuration.SinglePlayerDuties.RetryDifficulty;
                if (ImGui.Combo(_L("Difficulty when retrying a quest battle"), ref retryDifficulty, _retryDifficulties,
                        _retryDifficulties.Length))
                {
                    Configuration.SinglePlayerDuties.RetryDifficulty = (byte)retryDifficulty;
                    Save();
                }
            }
#endif
        }

        ImGui.Separator();

        using (ImRaii.Disabled(!runSoloInstancesWithBossMod))
        {
            ImGui.Text(
                _L("Questionable includes a default list of quest battles that work if BossMod is installed."));
            ImGui.Text(_L("The included list of quest battles can change with each update."));

            ImGui.Separator();
            ImGui.Text(_L("You can override the settings for each individual quest battle:"));


            using ImRaii.TabBarDisposable tabBar = ImRaii.TabBar("QuestionableConfigTabs");
            if (tabBar)
            {
                DrawMainScenarioConfigTable();
                DrawJobQuestConfigTable();
                DrawRoleQuestConfigTable();
                DrawOtherQuestConfigTable();
            }

            DrawEnableAllButton();
            ImGui.SameLine();
            DrawClipboardButtons();
            ImGui.SameLine();
            DrawResetButton();
        }
    }

    private void DrawMainScenarioConfigTable()
    {
        (int totalEnabled, int totalCount) = GetMainScenarioQuestCounts();
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_LF("Main Scenario Quests ({0}/{1})", totalEnabled, totalCount) + "###MSQ");
        if (!tab)
            return;

        using ImRaii.ChildDisposable child = BeginChildArea();
        if (!child)
            return;

        (int limsaEnabled, int limsaTotal) = GetQuestBattleCounts(_startingCityBattles[EAetheryteLocation.Limsa]);
        string limsaHeaderText = _L("Limsa Lominsa") + $" ({FormatLevel(5)} - {FormatLevel(14)}) ({limsaEnabled}/{limsaTotal})";
        string limsaKey = "Limsa";
        bool isLimsaHeaderOpen = Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(limsaKey, defaultValue: false);
        ImGui.SetNextItemOpen(isLimsaHeaderOpen, ImGuiCond.Always);
        if (ImGui.CollapsingHeader(limsaHeaderText))
        {
            if (!Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(limsaKey, defaultValue: false))
            {
                Configuration.SinglePlayerDuties.HeaderStates[limsaKey] = true;
                Save();
            }

            DrawQuestTable(_L("Limsa Lominsa"), _startingCityBattles[EAetheryteLocation.Limsa]);
        }
        else
        {
            if (Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(limsaKey, defaultValue: false))
            {
                Configuration.SinglePlayerDuties.HeaderStates[limsaKey] = false;
                Save();
            }
        }

        (int gridaniaEnabled, int gridaniaTotal) = GetQuestBattleCounts(_startingCityBattles[EAetheryteLocation.Gridania]);
        string gridaniaHeaderText = _L("Gridania") + $" ({FormatLevel(5)} - {FormatLevel(14)}) ({gridaniaEnabled}/{gridaniaTotal})";
        string gridaniaKey = "Gridania";
        bool isGridaniaHeaderOpen = Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(gridaniaKey, defaultValue: false);
        ImGui.SetNextItemOpen(isGridaniaHeaderOpen, ImGuiCond.Always);
        if (ImGui.CollapsingHeader(gridaniaHeaderText))
        {
            if (!Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(gridaniaKey, defaultValue: false))
            {
                Configuration.SinglePlayerDuties.HeaderStates[gridaniaKey] = true;
                Save();
            }

            DrawQuestTable(_L("Gridania"), _startingCityBattles[EAetheryteLocation.Gridania]);
        }
        else
        {
            if (Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(gridaniaKey, defaultValue: false))
            {
                Configuration.SinglePlayerDuties.HeaderStates[gridaniaKey] = false;
                Save();
            }
        }

        (int uldahEnabled, int uldahTotal) = GetQuestBattleCounts(_startingCityBattles[EAetheryteLocation.Uldah]);
        string uldahHeaderText = _L("Ul'dah") + $" ({FormatLevel(4)} - {FormatLevel(14)}) ({uldahEnabled}/{uldahTotal})";
        string uldahKey = "Uldah";
        bool isUldahHeaderOpen = Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(uldahKey, defaultValue: false);
        ImGui.SetNextItemOpen(isUldahHeaderOpen, ImGuiCond.Always);
        if (ImGui.CollapsingHeader(uldahHeaderText))
        {
            if (!Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(uldahKey, defaultValue: false))
            {
                Configuration.SinglePlayerDuties.HeaderStates[uldahKey] = true;
                Save();
            }

            DrawQuestTable(_L("Uldah"), _startingCityBattles[EAetheryteLocation.Uldah]);
        }
        else
        {
            if (Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(uldahKey, defaultValue: false))
            {
                Configuration.SinglePlayerDuties.HeaderStates[uldahKey] = false;
                Save();
            }
        }

        foreach (EExpansionVersion expansion in Enum.GetValues<EExpansionVersion>())
        {
            if (_mainScenarioBattles.TryGetValue(expansion, out List<SinglePlayerDutyInfo>? dutyInfos))
            {
                (int enabledCount, int totalCountForExpansion) = GetQuestBattleCounts(dutyInfos);
                string expansionHeaderText = $"{expansion.ToFriendlyString()} ({enabledCount}/{totalCountForExpansion})";
                string expansionKey = expansion.ToString();
                bool isExpansionHeaderOpen = Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(expansionKey, defaultValue: false);
                ImGui.SetNextItemOpen(isExpansionHeaderOpen, ImGuiCond.Always);
                if (ImGui.CollapsingHeader(expansionHeaderText))
                {
                    if (!Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(expansionKey, defaultValue: false))
                    {
                        Configuration.SinglePlayerDuties.HeaderStates[expansionKey] = true;
                        Save();
                    }

                    DrawQuestTable($"Duties{expansion}", dutyInfos);
                }
                else
                {
                    if (Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(expansionKey, defaultValue: false))
                    {
                        Configuration.SinglePlayerDuties.HeaderStates[expansionKey] = false;
                        Save();
                    }
                }
            }
        }
    }

    private void DrawJobQuestConfigTable()
    {
        (int totalEnabled, int totalCount) = GetJobQuestCounts();
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_LF("Class/Job Quests ({0}/{1})", totalEnabled, totalCount) + "###JobQuests");
        if (!tab)
            return;

        using ImRaii.ChildDisposable child = BeginChildArea();
        if (!child)
            return;

        int oldPriority = 0;
        foreach ((Job classJob, int priority) in _classJobUtils.SortedClassJobs)
        {
            if (classJob.IsCrafter() || classJob.IsGatherer())
                continue;

            if (_jobQuestBattles.TryGetValue(classJob, out List<SinglePlayerDutyInfo>? dutyInfos))
            {
                if (priority != oldPriority)
                {
                    oldPriority = priority;
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();
                }

                string jobName = classJob.ToFriendlyString();
                if (classJob.IsClass())
                    jobName += $" / {classJob.AsJob().ToFriendlyString()}";

                (int enabledCount, int totalCountForJob) = GetQuestBattleCounts(dutyInfos);
                string jobHeaderText = $"{jobName} ({enabledCount}/{totalCountForJob})";
                string jobKey = classJob.ToString();
                bool isJobHeaderOpen = Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(jobKey, defaultValue: false);
                ImGui.SetNextItemOpen(isJobHeaderOpen, ImGuiCond.Always);
                if (ImGui.CollapsingHeader(jobHeaderText))
                {
                    if (!Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(jobKey, defaultValue: false))
                    {
                        Configuration.SinglePlayerDuties.HeaderStates[jobKey] = true;
                        Save();
                    }

                    DrawQuestTable($"JobQuests{classJob}", dutyInfos);
                }
                else
                {
                    if (Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(jobKey, defaultValue: false))
                    {
                        Configuration.SinglePlayerDuties.HeaderStates[jobKey] = false;
                        Save();
                    }
                }
            }
        }
    }

    private void DrawRoleQuestConfigTable()
    {
        (int totalEnabled, int totalCount) = GetRoleQuestCounts();
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_LF("Role Quests ({0}/{1})", totalEnabled, totalCount) + "###RoleQuests");
        if (!tab)
            return;

        using ImRaii.ChildDisposable child = BeginChildArea();
        if (!child)
            return;

        foreach ((Job classJob, string label) in RoleQuestCategories)
        {
            if (_roleQuestBattles.TryGetValue(classJob, out List<SinglePlayerDutyInfo>? dutyInfos))
            {
                (int enabledCount, int totalCountForRole) = GetQuestBattleCounts(dutyInfos);
                string roleHeaderText = $"{label} ({enabledCount}/{totalCountForRole})";
                string roleKey = $"Role_{classJob}";
                bool isRoleHeaderOpen = Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(roleKey, defaultValue: false);
                ImGui.SetNextItemOpen(isRoleHeaderOpen, ImGuiCond.Always);
                if (ImGui.CollapsingHeader(roleHeaderText))
                {
                    if (!Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(roleKey, defaultValue: false))
                    {
                        Configuration.SinglePlayerDuties.HeaderStates[roleKey] = true;
                        Save();
                    }

                    DrawQuestTable($"RoleQuests{classJob}", dutyInfos);
                }
                else
                {
                    if (Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(roleKey, defaultValue: false))
                    {
                        Configuration.SinglePlayerDuties.HeaderStates[roleKey] = false;
                        Save();
                    }
                }
            }
        }

        (int otherEnabled, int otherTotal) = GetQuestBattleCounts(_otherRoleQuestBattles);
        string otherRoleHeaderText = _LF("General Role Quests ({0}/{1})", otherEnabled, otherTotal);
        string otherRoleKey = "Role_General";
        bool isOtherRoleHeaderOpen = Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(otherRoleKey, defaultValue: false);
        ImGui.SetNextItemOpen(isOtherRoleHeaderOpen, ImGuiCond.Always);
        if (ImGui.CollapsingHeader(otherRoleHeaderText))
        {
            if (!Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(otherRoleKey, defaultValue: false))
            {
                Configuration.SinglePlayerDuties.HeaderStates[otherRoleKey] = true;
                Save();
            }

            DrawQuestTable("RoleQuestsGeneral", _otherRoleQuestBattles);
        }
        else
        {
            if (Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(otherRoleKey, defaultValue: false))
            {
                Configuration.SinglePlayerDuties.HeaderStates[otherRoleKey] = false;
                Save();
            }
        }
    }

    private void DrawOtherQuestConfigTable()
    {
        (int totalEnabled, int totalCount) = GetOtherQuestCounts();
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_LF("Other Quests ({0}/{1})", totalEnabled, totalCount) + "###MiscQuests");
        if (!tab)
            return;

        using ImRaii.ChildDisposable child = BeginChildArea();
        if (!child)
            return;

        foreach ((string label, List<SinglePlayerDutyInfo> dutyInfos) in _otherQuestBattles)
        {
            (int enabledCount, int totalCountForCategory) = GetQuestBattleCounts(dutyInfos);
            string otherHeaderText = $"{label} ({enabledCount}/{totalCountForCategory})";
            string otherKey = $"Other_{label}";
            bool isOtherHeaderOpen = Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(otherKey, defaultValue: false);
            ImGui.SetNextItemOpen(isOtherHeaderOpen, ImGuiCond.Always);
            if (ImGui.CollapsingHeader(otherHeaderText))
            {
                if (!Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(otherKey, defaultValue: false))
                {
                    Configuration.SinglePlayerDuties.HeaderStates[otherKey] = true;
                    Save();
                }

                DrawQuestTable($"Other{label}", dutyInfos);
            }
            else
            {
                if (Configuration.SinglePlayerDuties.HeaderStates.GetValueOrDefault(otherKey, defaultValue: false))
                {
                    Configuration.SinglePlayerDuties.HeaderStates[otherKey] = false;
                    Save();
                }
            }
        }
    }

    private void DrawQuestTable(string label, IReadOnlyList<SinglePlayerDutyInfo> dutyInfos)
    {
        using ImRaii.TableDisposable table = ImRaii.Table(label, 2, ImGuiTableFlags.SizingFixedFit);
        if (table)
        {
            ImGui.TableSetupColumn(_L("Quest"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(_L("Options"), ImGuiTableColumnFlags.WidthFixed, 200f);

            foreach (SinglePlayerDutyInfo dutyInfo in dutyInfos)
            {
                ImGui.TableNextRow();

                string[] labels = dutyInfo.EnabledByDefault
                    ? SupportedCfcOptions
                    : UnsupportedCfcOptions;
                int value = 0;
                if (Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Contains(dutyInfo.ContentFinderConditionId))
                    value = 1;
                if (Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Contains(dutyInfo.ContentFinderConditionId))
                    value = 2;

                if (ImGui.TableNextColumn())
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(dutyInfo.Name);

                    if (ImGui.IsItemHovered() && Configuration.Advanced.AdditionalStatusInformation)
                    {
                        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
                        ImGui.TextUnformatted(dutyInfo.Name);
                        ImGui.Separator();
                        ImGui.BulletText(_LF("TerritoryId: {0}", dutyInfo.TerritoryId));
                        ImGui.BulletText(_LF("ContentFinderConditionId: {0}", dutyInfo.ContentFinderConditionId));
                    }

                    if (!dutyInfo.Enabled)
                    {
                        ImGuiComponents.HelpMarker(_L("Questionable doesn't include support for this quest yet."),
                            FontAwesomeIcon.Times, ImGuiColors.DalamudRed);
                    }
                    else if (dutyInfo.Notes.Count > 0)
                        DrawNotes(dutyInfo.EnabledByDefault, dutyInfo.Notes);
                }

                if (ImGui.TableNextColumn())
                {
                    using ImRaii.IdDisposable _ = ImRaii.PushId($"##Duty{dutyInfo.ContentFinderConditionId}");
                    using (ImRaii.Disabled(!dutyInfo.Enabled))
                    {
                        ImGui.SetNextItemWidth(200);
                        if (ImGui.Combo(string.Empty, ref value, labels, labels.Length))
                        {
                            Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Remove(dutyInfo.ContentFinderConditionId);
                            Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Remove(dutyInfo.ContentFinderConditionId);

                            if (value == 1)
                                Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Add(dutyInfo.ContentFinderConditionId);
                            else if (value == 2)
                                Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Add(dutyInfo.ContentFinderConditionId);

                            Save();
                        }
                    }
                }
            }
        }
    }

    private static ImRaii.ChildDisposable BeginChildArea() => ImRaii.Child("DutyConfiguration", new(675, 400), border: true);

    private void DrawEnableAllButton()
    {
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.CheckCircle, _L("Enable All")))
        {
            Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Clear();
            Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Clear();

            // Get all enabled quest battles and whitelist them
            IEnumerable<SinglePlayerDutyInfo> allEnabledDuties = GetAllEnabledSinglePlayerDuties();
            foreach (SinglePlayerDutyInfo duty in allEnabledDuties)
                Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Add(duty.ContentFinderConditionId);

            Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Enable all of the quest battles, use at your own risk."));
    }

    private void DrawClipboardButtons()
    {
        using (ImRaii.Disabled(Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Count +
            Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Count == 0))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Copy, _L("Export to clipboard")))
            {
                IEnumerable<string> whitelisted =
                    Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Select(x => $"{DutyWhitelistPrefix}{x}");
                IEnumerable<string> blacklisted =
                    Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Select(x => $"{DutyBlacklistPrefix}{x}");
                string text = SinglePlayerDutyClipboardPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    string.Join(DutyClipboardSeparator, whitelisted.Concat(blacklisted))));
                ImGui.SetClipboardText(text);
            }
        }

        ImGui.SameLine();

        string clipboardText = ImGui.GetClipboardText().Trim();
        using (ImRaii.Disabled(string.IsNullOrEmpty(clipboardText) ||
                               !clipboardText.StartsWith(SinglePlayerDutyClipboardPrefix, StringComparison.InvariantCulture)))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Paste, _L("Import from Clipboard")))
            {
                clipboardText = clipboardText.Substring(SinglePlayerDutyClipboardPrefix.Length);
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(clipboardText));

                Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Clear();
                Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Clear();
                foreach (string part in text.Split(DutyClipboardSeparator))
                {
                    if (part.StartsWith(DutyWhitelistPrefix) &&
                        uint.TryParse(part.AsSpan(start: 1), CultureInfo.InvariantCulture,
                            out uint whitelistedCfcId))
                    {
                        Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Add(whitelistedCfcId);
                    }

                    if (part.StartsWith(DutyBlacklistPrefix) &&
                        uint.TryParse(part.AsSpan(start: 1), CultureInfo.InvariantCulture,
                            out uint blacklistedCfcId))
                    {
                        Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Add(blacklistedCfcId);
                    }
                }

                Save();
            }
        }
    }

    private void DrawResetButton()
    {
        using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Undo, _L("Reset to default")))
            {
                Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Clear();
                Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Clear();
                Save();
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(_L("Hold CTRL to enable this button."));
    }

    private IEnumerable<SinglePlayerDutyInfo> GetAllEnabledSinglePlayerDuties()
    {
        return _startingCityBattles.Values.SelectMany(x => x)
            .Concat(_mainScenarioBattles.Values.SelectMany(x => x))
            .Concat(_jobQuestBattles.Values.SelectMany(x => x))
            .Concat(_roleQuestBattles.Values.SelectMany(x => x))
            .Concat(_otherRoleQuestBattles)
            .Concat(_otherQuestBattles.SelectMany(x => x.Item2))
            .Where(x => x.Enabled);
    }

    private (int enabledCount, int totalCount) GetQuestBattleCounts(IReadOnlyList<SinglePlayerDutyInfo> dutyInfos)
    {
        int enabledCount = 0;
        int totalCount = 0;

        foreach (SinglePlayerDutyInfo dutyInfo in dutyInfos)
        {
            if (dutyInfo.Enabled)
            {
                totalCount++;

                // a quest battle is considered "enabled" if:
                // it's whitelisted, OR
                // it's not blacklisted AND it's enabled by default
                bool isEnabled = Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Contains(dutyInfo.ContentFinderConditionId) ||
                                 (!Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Contains(dutyInfo.ContentFinderConditionId) && dutyInfo.EnabledByDefault);

                if (isEnabled)
                    enabledCount++;
            }
        }

        return (enabledCount, totalCount);
    }

    private (int enabledCount, int totalCount) GetMainScenarioQuestCounts()
    {
        int totalEnabled = 0;
        int totalCount = 0;

        // Count starting city battles
        foreach (List<SinglePlayerDutyInfo> battles in _startingCityBattles.Values)
        {
            (int enabled, int total) = GetQuestBattleCounts(battles);
            totalEnabled += enabled;
            totalCount += total;
        }

        // Count main scenario battles by expansion
        foreach (List<SinglePlayerDutyInfo> battles in _mainScenarioBattles.Values)
        {
            (int enabled, int total) = GetQuestBattleCounts(battles);
            totalEnabled += enabled;
            totalCount += total;
        }

        return (totalEnabled, totalCount);
    }

    private (int enabledCount, int totalCount) GetJobQuestCounts()
    {
        int totalEnabled = 0;
        int totalCount = 0;

        foreach (List<SinglePlayerDutyInfo> battles in _jobQuestBattles.Values)
        {
            (int enabled, int total) = GetQuestBattleCounts(battles);
            totalEnabled += enabled;
            totalCount += total;
        }

        return (totalEnabled, totalCount);
    }

    private (int enabledCount, int totalCount) GetRoleQuestCounts()
    {
        int totalEnabled = 0;
        int totalCount = 0;

        foreach (List<SinglePlayerDutyInfo> battles in _roleQuestBattles.Values)
        {
            (int enabled, int total) = GetQuestBattleCounts(battles);
            totalEnabled += enabled;
            totalCount += total;
        }

        (int otherEnabled, int otherTotal) = GetQuestBattleCounts(_otherRoleQuestBattles);
        totalEnabled += otherEnabled;
        totalCount += otherTotal;

        return (totalEnabled, totalCount);
    }

    private (int enabledCount, int totalCount) GetOtherQuestCounts()
    {
        int totalEnabled = 0;
        int totalCount = 0;

        foreach ((string _, List<SinglePlayerDutyInfo> battles) in _otherQuestBattles)
        {
            (int enabled, int total) = GetQuestBattleCounts(battles);
            totalEnabled += enabled;
            totalCount += total;
        }

        return (totalEnabled, totalCount);
    }

    private sealed record SinglePlayerDutyInfo
    (
        string Name,
        IQuestInfo QuestInfo,
        TerritoryData.ContentFinderConditionData ContentFinderConditionData,
        SinglePlayerDutyOptions Options,
        bool Enabled)
    {
        public EExpansionVersion Expansion => QuestInfo.Expansion;
        public uint JournalGenreId => QuestInfo.JournalGenre ?? uint.MaxValue;
        public ushort SortKey => QuestInfo.SortKey;
        public uint ContentFinderConditionId => ContentFinderConditionData.ContentFinderConditionId;
        public uint TerritoryId => ContentFinderConditionData.TerritoryId;
        public byte Index => Options.Index;
        public bool EnabledByDefault => Options.Enabled;
        public ReadOnlyCollection<string> Notes => Options.Notes.AsReadOnly();

        public bool IsLimsaStart => ContentFinderConditionId is 332 or 333 or 313 or 334;
        public bool IsGridaniaStart => ContentFinderConditionId is 296 or 297 or 299 or 298;
        public bool IsUldahStart => ContentFinderConditionId is 335 or 312 or 337 or 336;

        /// <summary>
        ///     'Other' role quest is the post-EW/DT role quests.
        /// </summary>
        public bool IsOtherRoleQuest => ContentFinderConditionId is 845 or 1016;
    }
}
