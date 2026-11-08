using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ExcelServices;
using Lumina.Excel.Sheets;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Windows.ConfigComponents;

[RegisterSingleton]
internal sealed class QuestBattleConfigComponent : ConfigComponent, IDisposable
{
    private const string ClipboardPrefix = "qst:single:";

    private static readonly List<(Job ClassJob, string Name)> RoleQuestCategories =
    [
        (Job.PLD, _L("Tank Role Quests")),
        (Job.WHM, _L("Healer Role Quests")),
        (Job.LNC, _L("Melee Role Quests")),
        (Job.BRD, _L("Physical Ranged Role Quests")),
        (Job.BLM, _L("Magical Ranged Role Quests"))
    ];
    private Vector2 _contentSize;

#if false
    private readonly string[] _retryDifficulties = [_L("Normal"), _L("Easy"), _L("Very Easy")];
#endif

    private readonly TerritoryData _territoryData;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestData _questData;
    private readonly IDataManager _dataManager;
    private readonly ClassJobUtils _classJobUtils;
    private readonly ILogger<QuestBattleConfigComponent> _logger;

    private ImmutableDictionary<EAetheryteLocation, List<BattleInfo>> _startingCityBattles =
        ImmutableDictionary<EAetheryteLocation, List<BattleInfo>>.Empty;

    private ImmutableDictionary<EExpansionVersion, List<BattleInfo>> _mainScenarioBattles =
        ImmutableDictionary<EExpansionVersion, List<BattleInfo>>.Empty;

    private ImmutableDictionary<Job, List<BattleInfo>> _jobQuestBattles =
        ImmutableDictionary<Job, List<BattleInfo>>.Empty;

    private ImmutableDictionary<Job, List<BattleInfo>> _roleQuestBattles =
        ImmutableDictionary<Job, List<BattleInfo>>.Empty;

    private ImmutableList<BattleInfo> _otherRoleQuestBattles = ImmutableList<BattleInfo>.Empty;

    private ImmutableList<(string Label, List<BattleInfo>)> _otherQuestBattles =
        ImmutableList<(string Label, List<BattleInfo>)>.Empty;

    public QuestBattleConfigComponent(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        TerritoryData territoryData,
        QuestRegistry questRegistry,
        QuestData questData,
        IDataManager dataManager,
        ClassJobUtils classJobUtils,
        ILogger<QuestBattleConfigComponent> logger) : base(pluginInterface, configuration)
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
        List<ElementId> questsWithMultipleBattles = _territoryData.GetAllQuestBattles()
            .GroupBy(x => x.QuestId)
            .Where(x => x.Skip(1).Any())
            .Select(x => x.Key)
            .ToList();

        List<BattleInfo> mainScenarioBattles = [];
        Dictionary<EAetheryteLocation, List<BattleInfo>> startingCityBattles =
            new()
            {
                { EAetheryteLocation.Limsa, [] },
                { EAetheryteLocation.Gridania, [] },
                { EAetheryteLocation.Uldah, [] }
            };

        List<BattleInfo> otherBattles = [];

        Dictionary<ElementId, Job> questIdsToJob = Enum.GetValues<Job>()
            .Where(x => x != Job.ADV && !x.IsCrafter() && !x.IsGatherer() && (x.IsClass() || !x.HasBaseClass()))
            .SelectMany(x => _questRegistry.GetKnownClassJobQuests(x, includeRoleQuests: false)
            .Select(y => (y.QuestId, ClassJob: x)))
            .ToDictionary(x => x.QuestId, x => x.ClassJob);
        Dictionary<Job, List<BattleInfo>> jobQuestBattles = questIdsToJob.Values.Distinct()
            .ToDictionary(x => x, _ => new List<BattleInfo>());

        Dictionary<ElementId, List<Job>> questIdToRole = RoleQuestCategories
            .SelectMany(x => _questData.GetRoleQuests(x.ClassJob).Select(y => (y.QuestId, x.ClassJob)))
            .GroupBy(x => x.QuestId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.ClassJob).ToList());
        Dictionary<Job, List<BattleInfo>> roleQuestBattles = RoleQuestCategories
            .ToDictionary(x => x.ClassJob, _ => new List<BattleInfo>());
        List<BattleInfo> otherRoleQuestBattles = [];

        foreach ((ElementId questId, byte index, TerritoryData.CfcData cfcData) in _territoryData.GetAllQuestBattles())
        {
            IQuestInfo questInfo = _questData.GetQuestInfo(questId);
            (bool enabled, SinglePlayerDutyOptions options) = FindDutyOptions(questId, index);

            string name = $"{FormatLevel(questInfo.Level)} {questInfo.Name}";
            if (!string.IsNullOrEmpty(cfcData.Name) && !questInfo.Name.EndsWith(cfcData.Name, StringComparison.Ordinal))
                name += _LF(" ({0})", cfcData.Name);

            if (questsWithMultipleBattles.Contains(questId))
                name += _LF(" (Part {0})", options.Index + 1);
            else if (cfcData.Id is 674 or 691)
                name += _L(" (Melee/Phys. Ranged)");

            BattleInfo battle = new(name, questInfo, cfcData, options, enabled);

            if (battle.IsLimsaStart)
                startingCityBattles[EAetheryteLocation.Limsa].Add(battle);
            else if (battle.IsGridaniaStart)
                startingCityBattles[EAetheryteLocation.Gridania].Add(battle);
            else if (battle.IsUldahStart)
                startingCityBattles[EAetheryteLocation.Uldah].Add(battle);
            else if (questInfo.IsMainScenarioQuest)
                mainScenarioBattles.Add(battle);
            else if (questIdsToJob.TryGetValue(questId, out Job classJob))
                jobQuestBattles[classJob].Add(battle);
            else if (questIdToRole.TryGetValue(questId, out List<Job>? classJobs))
            {
                foreach (Job roleClassJob in classJobs)
                    roleQuestBattles[roleClassJob].Add(battle);
            }
            else if (battle.IsOtherRoleQuest)
                otherRoleQuestBattles.Add(battle);
            else
                otherBattles.Add(battle);
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
                        .DistinctBy(y => y.CfcId)
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
        _contentSize = ImGui.GetWindowContentRegionMax();
        using var wrap = ImRaii.TextWrapPos(_contentSize.X + 10);

        bool useBossMod = Configuration.SinglePlayerDuties.RunSoloInstancesWithBossMod;
        if (ImGui.Checkbox(_L("Run quest battles with BossMod"), ref useBossMod))
        {
            Configuration.SinglePlayerDuties.RunSoloInstancesWithBossMod = useBossMod;
            Save();
        }

        using (ImRaii.PushIndent(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X))
        {
            using (_ = ImRaii.PushColor(ImGuiCol.Text, QstTheme.Danger))
            {
                ImGui.TextUnformatted(_L("Notes:"));
                ImGui.BulletText(_L("Will always use BossMod for combat (ignoring the configured combat module)."));
                ImGui.BulletText(_L("Retries after a failed attempt always start at 'Very Easy' difficulty. The first attempt may start at 'Normal'."));
                ImGui.BulletText(_L("BossMod forks (such as Reborn) are NOT COMPATIBLE with Questionable."));
            }

#if false
            using (ImRaii.Disabled(!useBossMod))
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

        using (ImRaii.Disabled(!useBossMod))
        {
            ImGui.Text(
                _L("Questionable includes a default list of quest battles that work if BossMod is installed."));
            ImGui.Text(_L("The included list of quest battles can change with each update."));

            ImGui.Separator();
            ImGui.Text(_L("You can override the settings for each individual quest battle:"));


            using ImRaii.TabBarDisposable tabBar = ImRaii.TabBar("QuestionableConfigTabs");
            if (tabBar)
            {
                DrawMainScenarioTab();
                DrawJobQuestTab();
                DrawRoleQuestTab();
                DrawOtherQuestTab();
            }

            DrawEnableAllButton();
            ImGui.SameLine();
            DrawResetButton(Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds,
                Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds);
            if (_contentSize.X > 500)
                ImGui.SameLine();
            DrawClipboardButtons(ClipboardPrefix, Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds,
                Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds);
        }
    }

    private void DrawMainScenarioTab()
    {
        (int totalEnabled, int totalCount) = GetMainScenarioCounts();
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_LF("Main Scenario Quests ({0}/{1})", totalEnabled, totalCount) + "###MSQ");
        if (!tab)
            return;

        using ImRaii.ChildDisposable child = BeginChildArea(_contentSize.X);
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
            if (_mainScenarioBattles.TryGetValue(expansion, out List<BattleInfo>? battles))
            {
                (int enabledCount, int totalCountForExpansion) = GetQuestBattleCounts(battles);
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

                    DrawQuestTable($"Duties{expansion}", battles);
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

    private void DrawJobQuestTab()
    {
        (int totalEnabled, int totalCount) = GetJobQuestCounts();
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_LF("Class/Job Quests ({0}/{1})", totalEnabled, totalCount) + "###JobQuests");
        if (!tab)
            return;

        using ImRaii.ChildDisposable child = BeginChildArea(_contentSize.X);
        if (!child)
            return;

        int oldPriority = 0;
        foreach ((Job classJob, int priority) in _classJobUtils.SortedJobs)
        {
            if (classJob.IsCrafter() || classJob.IsGatherer())
                continue;

            if (_jobQuestBattles.TryGetValue(classJob, out List<BattleInfo>? battles))
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

                (int enabledCount, int totalCountForJob) = GetQuestBattleCounts(battles);
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

                    DrawQuestTable($"JobQuests{classJob}", battles);
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

    private void DrawRoleQuestTab()
    {
        (int totalEnabled, int totalCount) = GetRoleQuestCounts();
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_LF("Role Quests ({0}/{1})", totalEnabled, totalCount) + "###RoleQuests");
        if (!tab)
            return;

        using ImRaii.ChildDisposable child = BeginChildArea(_contentSize.X);
        if (!child)
            return;

        foreach ((Job classJob, string label) in RoleQuestCategories)
        {
            if (_roleQuestBattles.TryGetValue(classJob, out List<BattleInfo>? battles))
            {
                (int enabledCount, int totalCountForRole) = GetQuestBattleCounts(battles);
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

                    DrawQuestTable($"RoleQuests{classJob}", battles);
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

    private void DrawOtherQuestTab()
    {
        (int totalEnabled, int totalCount) = GetOtherQuestCounts();
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_LF("Other Quests ({0}/{1})", totalEnabled, totalCount) + "###MiscQuests");
        if (!tab)
            return;

        using ImRaii.ChildDisposable child = BeginChildArea(_contentSize.X);
        if (!child)
            return;

        foreach ((string label, List<BattleInfo> battles) in _otherQuestBattles)
        {
            (int enabledCount, int totalCountForCategory) = GetQuestBattleCounts(battles);
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

                DrawQuestTable($"Other{label}", battles);
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

    private void DrawQuestTable(string label, IReadOnlyList<BattleInfo> battles)
    {
        using ImRaii.TableDisposable table = ImRaii.Table(label, 2, ImGuiTableFlags.SizingFixedFit);
        if (table)
        {
            ImGui.TableSetupColumn(_L("Quest"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(_L("Options"), ImGuiTableColumnFlags.WidthFixed, 200f);

            foreach (BattleInfo battle in battles)
            {
                ImGui.TableNextRow();

                string[] labels = battle.EnabledByDefault
                    ? SupportedCfcOptions
                    : UnsupportedCfcOptions;
                int value = 0;
                if (Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Contains(battle.CfcId))
                    value = 1;
                if (Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Contains(battle.CfcId))
                    value = 2;

                if (ImGui.TableNextColumn())
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(battle.Name);

                    if (ImGui.IsItemHovered() && Configuration.Advanced.AdditionalStatusInformation)
                    {
                        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
                        ImGui.TextUnformatted(battle.Name);
                        ImGui.Separator();
                        ImGui.BulletText(_LF("TerritoryId: {0}", battle.TerritoryId));
                        ImGui.BulletText(_LF("ContentFinderConditionId: {0}", battle.CfcId));
                    }

                    if (!battle.Enabled)
                    {
                        ImGuiComponents.HelpMarker(_L("Questionable doesn't include support for this quest yet."),
                            FontAwesomeIcon.Times, QstTheme.Danger);
                    }
                    else if (battle.Notes.Count > 0)
                        DrawNotes(battle.EnabledByDefault, battle.Notes);
                }

                if (ImGui.TableNextColumn())
                {
                    using ImRaii.IdDisposable _ = ImRaii.PushId($"##Duty{battle.CfcId}");
                    using (ImRaii.Disabled(!battle.Enabled))
                    {
                        ImGui.SetNextItemWidth(200);
                        if (ImGui.Combo(string.Empty, ref value, labels, labels.Length))
                        {
                            Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Remove(battle.CfcId);
                            Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Remove(battle.CfcId);

                            if (value == 1)
                                Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Add(battle.CfcId);
                            else if (value == 2)
                                Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Add(battle.CfcId);

                            Save();
                        }
                    }
                }
            }
        }
    }

    private static ImRaii.ChildDisposable BeginChildArea(float width) => ImRaii.Child("DutyConfiguration", new(width - 5, 300), border: true);

    private void DrawEnableAllButton()
    {
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.CheckCircle, _L("Enable All")))
        {
            Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Clear();
            Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Clear();

            IEnumerable<BattleInfo> enabledBattles = GetEnabledBattles();
            foreach (BattleInfo battle in enabledBattles)
                Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Add(battle.CfcId);

            Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Enable all of the quest battles, use at your own risk."));
    }

    private IEnumerable<BattleInfo> GetEnabledBattles()
    {
        return _startingCityBattles.Values.SelectMany(x => x)
            .Concat(_mainScenarioBattles.Values.SelectMany(x => x))
            .Concat(_jobQuestBattles.Values.SelectMany(x => x))
            .Concat(_roleQuestBattles.Values.SelectMany(x => x))
            .Concat(_otherRoleQuestBattles)
            .Concat(_otherQuestBattles.SelectMany(x => x.Item2))
            .Where(x => x.Enabled);
    }

    private (int enabledCount, int totalCount) GetQuestBattleCounts(IReadOnlyList<BattleInfo> battles)
    {
        int enabledCount = 0;
        int totalCount = 0;

        foreach (BattleInfo battle in battles)
        {
            if (battle.Enabled)
            {
                totalCount++;

                bool isEnabled = Configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Contains(battle.CfcId) ||
                                 (!Configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Contains(battle.CfcId) && battle.EnabledByDefault);

                if (isEnabled)
                    enabledCount++;
            }
        }

        return (enabledCount, totalCount);
    }

    private (int enabledCount, int totalCount) GetMainScenarioCounts()
    {
        int totalEnabled = 0;
        int totalCount = 0;

        foreach (List<BattleInfo> battles in _startingCityBattles.Values)
        {
            (int enabled, int total) = GetQuestBattleCounts(battles);
            totalEnabled += enabled;
            totalCount += total;
        }

        foreach (List<BattleInfo> battles in _mainScenarioBattles.Values)
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

        foreach (List<BattleInfo> battles in _jobQuestBattles.Values)
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

        foreach (List<BattleInfo> battles in _roleQuestBattles.Values)
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

        foreach ((string _, List<BattleInfo> battles) in _otherQuestBattles)
        {
            (int enabled, int total) = GetQuestBattleCounts(battles);
            totalEnabled += enabled;
            totalCount += total;
        }

        return (totalEnabled, totalCount);
    }

    private sealed record BattleInfo
    (
        string Name,
        IQuestInfo QuestInfo,
        TerritoryData.CfcData CfcData,
        SinglePlayerDutyOptions Options,
        bool Enabled)
    {
        public EExpansionVersion Expansion => QuestInfo.Expansion;
        public uint JournalGenreId => QuestInfo.JournalGenre ?? uint.MaxValue;
        public ushort SortKey => QuestInfo.SortKey;
        public uint CfcId => CfcData.Id;
        public uint TerritoryId => CfcData.TerritoryId;
        public byte Index => Options.Index;
        public bool EnabledByDefault => Options.Enabled;
        public ReadOnlyCollection<string> Notes => Options.Notes.AsReadOnly();

        public bool IsLimsaStart => CfcId is 332 or 333 or 313 or 334;
        public bool IsGridaniaStart => CfcId is 296 or 297 or 299 or 298;
        public bool IsUldahStart => CfcId is 335 or 312 or 337 or 336;

        // Post-EW/DT role quests.
        public bool IsOtherRoleQuest => CfcId is 845 or 1016;
    }
}
