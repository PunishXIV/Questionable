using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using Lumina.Excel.Sheets;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;
namespace Questionable.Windows.ConfigComponents;

[RegisterSingleton]
internal sealed class GeneralConfigComponent : ConfigComponent
{
    private static readonly (uint Id, string Name) DefaultMount = (0, _L("Mount Roulette"));
    private static readonly (Job ClassJob, string Name) DefaultClassJob = (Job.ADV, _L("Auto (highest level/item level)"));

    private readonly string[] _grandCompanyNames =
        [_L("Auto"), _L("Maelstrom"), _L("Twin Adder"), _L("Immortal Flames")];

    private readonly QuestRegistry _questRegistry;
    private readonly TerritoryData _territoryData;
    private readonly PathDataUpdater _pathDataUpdater;
    private readonly IDataManager _dataManager;
    private readonly ItemBlacklistSelector _itemBlacklistSelector;
    private uint? _itemToRemove;
    private readonly Lazy<List<Job>> _sortedClassJobs;
    private readonly Lazy<(uint[] Ids, string[] Names)> _mounts;
    private readonly Lazy<(Job[] Ids, string[] Names)> _classJobs;
    private readonly Lazy<(Job[] Ids, string[] Names)> _craftJobs;
    private readonly Lazy<(Job[] Ids, string[] Names)> _gatherJobs;
    private string _mountSearchString = string.Empty;
    private string _langSearchString = string.Empty;

    public GeneralConfigComponent(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        IDataManager dataManager,
        ClassJobUtils classJobUtils,
        QuestRegistry questRegistry,
        TerritoryData territoryData,
        PathDataUpdater pathDataUpdater)
        : base(pluginInterface, configuration)
    {
        _questRegistry = questRegistry;
        _territoryData = territoryData;
        _pathDataUpdater = pathDataUpdater;
        _dataManager = dataManager;
        _itemBlacklistSelector = new(dataManager);

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
            .OrderBy(x => x.Name, StringComparer.Ordinal)
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

        return ([.. jobs], [.. jobs.Select(x => x.ToString())]);
    }

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("General") + "###General");
        if (!tab)
            return;
        var size = ImGui.GetWindowContentRegionMax();
        using var _ = ImRaii.TextWrapPos(size.X);
        Dictionary<string, string> languages = new(StringComparer.Ordinal){
            { "en",    _L("English") },
            { "ja-jp", _L("Japanese") },
            { "zh-cn", _L("Chinese (Simplified)") },
            { "zh-tw", _L("Chinese (Traditional)") },
            { "ko",    _L("Korean") + " (WIP)" }
        };
        string language = Configuration.General.Language;
        if (ImGuiComponentsLocal.DrawSearchableCombo(_L("Language"), languages.Keys.ToArray(), languages.Values.ToArray(),
            Configuration.General.Language, ref _langSearchString, ref language))
        {
            Configuration.SetLanguage(language);
            Save();
        }

        if (QstWidgets.SectionHeader(_L("Preferences"), "Preferences", defaultOpen: false))
        {
            (uint[] mountIds, string[] mountNames) = _mounts.Value;
            uint mountId = Configuration.General.MountId;
            ImGui.SetNextItemWidth(size.X / 2);
            if (ImGuiComponentsLocal.DrawSearchableCombo(_L("Preferred Mount"), mountIds, mountNames,
                Configuration.General.MountId, ref _mountSearchString, ref mountId))
            {
                Configuration.General.MountId = mountId;
                Save();
            }

            int grandCompany = (int)Configuration.General.GrandCompany;
            ImGui.SetNextItemWidth(size.X / 2);
            if (ImGui.Combo(_L("Preferred Grand Company"), ref grandCompany, _grandCompanyNames,
                _grandCompanyNames.Length))
            {
                Configuration.General.GrandCompany = (GrandCompany)grandCompany;
                Save();
            }

            (Job[] classJobIds, string[] classJobNames) = _classJobs.Value;
            ImGui.SetNextItemWidth(size.X / 2);
            DrawComboOption(_L("Preferred Combat Job"), classJobIds, classJobNames,
                () => Configuration.General.CombatJob,
                v => Configuration.General.CombatJob = v);

            (Job[] craftJobIds, string[] craftJobNames) = _craftJobs.Value;
            ImGui.SetNextItemWidth(size.X / 2);
            DrawComboOption(_L("Preferred Crafting Job"), craftJobIds, craftJobNames,
                () => Configuration.General.CraftingJob,
                v => Configuration.General.CraftingJob = v);

            (Job[] gatherJobIds, string[] gatherJobNames) = _gatherJobs.Value;
            ImGui.SetNextItemWidth(size.X / 2);
            DrawComboOption(_L("Preferred Gathering Job"), gatherJobIds, gatherJobNames,
                () => Configuration.General.GatheringJob,
                v => Configuration.General.GatheringJob = v);

            DrawCheckbox(_L("Namazu: prefer Crafting job over Gatherer"),
                () => Configuration.Advanced.NamazuPreferCraft,
                value => Configuration.Advanced.NamazuPreferCraft = value,
                _L("Namazu tribe quests can be done as either DoH or DoL, this lets you set that preference."));

            ImGui.BeginGroup();
            using (ImRaii.Disabled(!StylistIpc.IsInstalled))
            {
                EGearsetUpdateSource gearsetSource = Configuration.General.GearsetUpdateSource;
                ImGui.SetNextItemWidth(size.X / 2);
                if (ImGuiEx.EnumCombo(_L("Preferred Gear Upgrade Source"), ref gearsetSource))
                {
                    Configuration.General.GearsetUpdateSource = gearsetSource;
                    Save();
                }
                // A missing Stylist keeps the saved choice; gear steps use Vanilla until it is installed again.
            }
            ImGui.EndGroup();
            if (!StylistIpc.IsInstalled && ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Stylist is not installed."));

            string chocoboName = Configuration.General.ChocoboName;
            ImGui.SetNextItemWidth(size.X / 2);
            var genName = NameGenerator.GenerateFirstName(fast: grandCompany == 0);
            if (ImGui.InputTextEx(_L("Chocobo name"), genName, ref chocoboName, 20))
                Configuration.General.ChocoboName = chocoboName;

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                if (string.IsNullOrWhiteSpace(Configuration.General.ChocoboName))
                    Configuration.General.ChocoboName = genName;
                Save();
            }

            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text(_L("The name to give your chocobo during the \"My Little Chocobo\" quest."));
                    ImGui.Text(_L("Generates a random name if invalid or left blank."));
                }
            }
        }

        if (QstWidgets.SectionHeader(_L("UI"), "UI", defaultOpen: false))
        {
            using (ImRaii.PushIndent())
            {
                bool useQuestionableTheme = Configuration.General.UseQuestionableTheme;
                if (ImGui.Checkbox(_L("Use Questionable theme"), ref useQuestionableTheme))
                {
                    Configuration.General.UseQuestionableTheme = useQuestionableTheme;
                    Save();
                }

                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.Text(_L("When disabled, Questionable's windows use your Dalamud style/theme instead."));
                    }
                }

                bool showIncompleteSeasonalEvents = Configuration.General.ShowIncompleteSeasonalEvents;
                if (ImGui.Checkbox(_L("Show details for incomplete seasonal events"), ref showIncompleteSeasonalEvents))
                {
                    Configuration.General.ShowIncompleteSeasonalEvents = showIncompleteSeasonalEvents;
                    Save();
                }

                using (ImRaii.Disabled(!showIncompleteSeasonalEvents))
                {
                    bool showCompleteSeasonalEvents = Configuration.General.ShowCompleteSeasonalEvents;
                    if (ImGui.Checkbox(_L("Continue showing completed seasonal events"), ref showCompleteSeasonalEvents))
                    {
                        Configuration.General.ShowCompleteSeasonalEvents = showCompleteSeasonalEvents;
                        Save();
                    }
                }

                bool questIcons = Configuration.General.QuestIcons;
                if (ImGui.Checkbox(_L("Show quest icons"), ref questIcons))
                {
                    Configuration.General.QuestIcons = questIcons;
                    Save();
                }

                bool hideInAllInstances = Configuration.General.HideInAllInstances;
                if (ImGui.Checkbox(_L("Hide quest window in all instanced duties"), ref hideInAllInstances))
                {
                    Configuration.General.HideInAllInstances = hideInAllInstances;
                    Save();
                }

                bool hideSponsorButton = Configuration.General.HideSponsorButton;
                if (ImGui.Checkbox(_L("Hide Sponsor button"), ref hideSponsorButton))
                {
                    Configuration.General.HideSponsorButton = hideSponsorButton;
                    Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_L("Takes effect next time the plugin is loaded"));

                bool hideRemainingTasks = Configuration.General.HideRemainingTasks;
                if (ImGui.Checkbox(_L("Hide remaining tasks"), ref hideRemainingTasks))
                {
                    Configuration.General.HideRemainingTasks = hideRemainingTasks;
                    Save();
                }

                bool hidePatch = Configuration.General.HidePatch;
                if (ImGui.Checkbox(_L("Hide quest patch badge"), ref hidePatch))
                {
                    Configuration.General.HidePatch = hidePatch;
                    Save();
                }

                bool hideLevel = Configuration.General.HideQuestRequiredLevel;
                if (ImGui.Checkbox(_L("Hide quest level badge"), ref hideLevel))
                {
                    Configuration.General.HideQuestRequiredLevel = hideLevel;
                    Save();
                }

                bool hideQuestStartedJob = Configuration.General.HideQuestStartedJob;
                if (ImGui.Checkbox(_L("Hide which job a quest was started with"), ref hideQuestStartedJob))
                {
                    Configuration.General.HideQuestStartedJob = hideQuestStartedJob;
                    Save();
                }

                bool hideThingsNotAvailable = Configuration.General.HideThingsNotAvailable;
                if (ImGui.Checkbox(_L("Hide things unavailable in your account's unlocked expansions"), ref hideThingsNotAvailable))
                {
                    Configuration.General.HideThingsNotAvailable = hideThingsNotAvailable;
                    Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_L("If you are on free trial, or do not own the current expansion, this will hide content in locked expansions"));

                DrawCheckbox(_L("Show window on start"),
                    () => Configuration.Advanced.ShowWindowOnStart,
                    value => Configuration.Advanced.ShowWindowOnStart = value,
                    _L("When enabled, Questionable's progress window will show when the plugin is loaded."));

                DrawCheckbox(_L("Start minimized"),
                    () => Configuration.Advanced.StartMinimized,
                    value => Configuration.Advanced.StartMinimized = value,
                    _L("When enabled, Questionable's progress window will be in its minimized state when loaded."));
            }
        }

#if REPORTING
        ImGui.Separator();
        ImGui.Text(_L("Bug Report"));
        using (ImRaii.PushIndent())
        {
            bool reportOptOut = Configuration.General.ReportsDisabled;
            if (ImGui.Checkbox(_L("Opt out of bug reports"), ref reportOptOut))
            {
                Configuration.General.ReportsDisabled = reportOptOut;
                Configuration.General.DismissedReportWarning = true;
                Save();
            }

            bool dismissedReportWarning = Configuration.General.DismissedReportWarning;
            if (ImGui.Checkbox(_L("Hide Report warning"), ref dismissedReportWarning))
            {
                Configuration.General.DismissedReportWarning = dismissedReportWarning;
                Save();
            }

            if (!reportOptOut)
            {
                string reportMessage = Configuration.General.ReportMessage;
                if (ImGui.InputText(_L("Report message"), ref reportMessage, 256))
                {
                    Configuration.General.ReportMessage = reportMessage;
                    Save();
                }
            }
        }
#endif

        if (QstWidgets.SectionHeader(_L("Questing"), "Questing", defaultOpen: false))
        {
            using (ImRaii.PushIndent())
            {
                bool configureTextAdvance = Configuration.General.ConfigureTextAdvance;
                if (ImGui.Checkbox(_L("Automatically configure TextAdvance with the recommended settings"),
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
                        if (ImGui.Checkbox(_L("but don't skip cutscenes or dialogue"), ref dontSkipCutscenes))
                        {
                            Configuration.General.DontSkipCutscenes = dontSkipCutscenes;
                            Save();
                        }
                    }
                }
                bool dontShowAnswerSuggestions = Configuration.General.DontShowAnswerSuggestions;
                if (ImGui.Checkbox(_L("Hide dialogue answer suggestions/notifications"), ref dontShowAnswerSuggestions))
                {
                    Configuration.General.DontShowAnswerSuggestions = dontShowAnswerSuggestions;
                    Save();
                }

                bool useEscToCancelQuesting = Configuration.General.UseEscToCancelQuesting;
                if (ImGui.Checkbox(_L("Use ESC to cancel questing/movement"), ref useEscToCancelQuesting))
                {
                    Configuration.General.UseEscToCancelQuesting = useEscToCancelQuesting;
                    Save();
                }

                bool useTickets = Configuration.General.UseTickets;
                if (ImGui.Checkbox(_L("Use aetheryte tickets where available"), ref useTickets))
                {
                    Configuration.General.UseTickets = useTickets;
                    Save();
                }

                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.Text(_L("Ideally this should be set in the in-game Teleport settings, but is provided here for convenience."));
                    }
                }

                bool confirmCurrencyOverCap = Configuration.General.ConfirmCurrencyOverCap;
                if (ImGui.Checkbox(_L("Accept losing gil, company seals or tomestones over the cap"), ref confirmCurrencyOverCap))
                {
                    Configuration.General.ConfirmCurrencyOverCap = confirmCurrencyOverCap;
                    Save();
                }

                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.Text(_L("When a reward would go over the cap, the game asks before discarding the excess. Off: Questionable stops so you can spend them first. On: it confirms and continues."));
                    }
                }

                bool sameJobThroughoutQuest = Configuration.General.SameJobThroughoutQuest;
                if (ImGui.Checkbox(_L("Switch back to the job a quest was started with before each interaction"), ref sameJobThroughoutQuest))
                {
                    Configuration.General.SameJobThroughoutQuest = sameJobThroughoutQuest;
                    Save();
                }

                bool ignoreNextQuestIfPrioSet = Configuration.General.IgnoreNextQuestIfPrioSet;
                if (ImGui.Checkbox(_L("When a quest finishes, do ready priority quests before its follow-up quest"), ref ignoreNextQuestIfPrioSet))
                {
                    Configuration.General.IgnoreNextQuestIfPrioSet = ignoreNextQuestIfPrioSet;
                    Save();
                }
#if false
            ImGui.Spacing();
            bool autoStepRefreshEnabled = Configuration.General.AutoStepRefreshEnabled;
            if (ImGui.Checkbox(_L("Automatically refresh quest steps when stuck (WIP see tooltip)"), ref autoStepRefreshEnabled))
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
                    ImGui.Text(_L("Questionable will automatically refresh a quest step if it appears to be stuck after the configured delay."));
                    ImGui.Text(_L("This helps resume automated quest completion when interruptions occur."));
                    ImGui.Text(_L("WIP feature, rather than remove it, this is a warning that it isn't fully complete."));
                }
            }

            using (ImRaii.Disabled(!autoStepRefreshEnabled))
            {
                ImGui.Indent();
                int autoStepRefreshDelay = Configuration.General.AutoStepRefreshDelaySeconds;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.SliderInt(_L("Refresh delay (seconds)"), ref autoStepRefreshDelay, 30, 180))
                {
                    Configuration.General.AutoStepRefreshDelaySeconds = autoStepRefreshDelay;
                    Save();
                }

                ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                    _LF("Quest steps will refresh automatically after {0} seconds if no progress is made.", autoStepRefreshDelay));
                ImGui.Unindent();
            }
#endif
            }
        }

        DrawOptionalQuests();
        DrawRewardRedemption();
        DrawUpdates();
    }

    private void DrawOptionalQuests()
    {
        if (!QstWidgets.SectionHeader(_L("Optional quests"), "OptionalQuests", defaultOpen: false))
            return;

        using (ImRaii.PushIndent())
        {
            DrawPickUp(_L("Pick up aether currents and aether current quests"),
                () => Configuration.Advanced.SkipAetherCurrents,
                value => Configuration.Advanced.SkipAetherCurrents = value,
                _L("If not done during the MSQ by Questionable, you have to manually pick up any missed aether currents/quests. You can set Questionable to run all pending aether current quests using the Priority Quests preset system."));

            DrawPickUp(_L("Pick up class/job/role quests"),
                () => Configuration.Advanced.SkipClassJobQuests,
                value => Configuration.Advanced.SkipClassJobQuests = value,
                _L("Class and job skills for A Realm Reborn, Heavensward and (for the Lv70 skills) Stormblood are locked behind quests. Not recommended if you plan on queueing for instances with duty finder/party finder."));

            DrawPickUp(_L("Pick up ARR hard mode primal quests"),
                () => Configuration.Advanced.SkipARealmRebornHardModePrimals,
                value => Configuration.Advanced.SkipARealmRebornHardModePrimals = value,
                _L("Hard mode Ifrit/Garuda/Titan are required for the Patch 2.5 quest 'Good Intentions' and to start Heavensward."));

            DrawPickUp(_L("Pick up Crystal Tower quests"),
                () => Configuration.Advanced.SkipCrystalTowerRaids,
                value => Configuration.Advanced.SkipCrystalTowerRaids = value,
                _L("Crystal Tower raids are required for the Patch 2.55 quest 'A Time to Every Purpose' and to start Heavensward."));

            DrawPickUp(_L("Pick up \"My Feisty Little Chocobo\""),
                () => Configuration.Advanced.SkipFeistyLittleChocobo,
                value => Configuration.Advanced.SkipFeistyLittleChocobo = value,
                _L("'My Feisty Little Chocobo' lets your companion chocobo fight with you in the overworld. When ticked, it is done automatically after 'My Little Chocobo'."));

            bool skipLowPriorityInstances = Configuration.General.SkipLowPriorityDuties;
            if (ImGui.Checkbox(_L("Unlock certain optional dungeons and raids (instead of waiting for completion)"), ref skipLowPriorityInstances))
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
                    ImGui.Text(_L("Questionable automatically picks up some optional quests (e.g. for aether currents, or the ARR alliance raids)."));
                    ImGui.Text(_L("If this setting is enabled, Questionable will continue with other quests, instead of waiting for manual completion of the duty."));

                    ImGui.Separator();
                    ImGui.Text(_L("This affects the following dungeons and raids:"));
                    foreach ((uint ContentFinderConditionId, ElementId QuestId, int Sequence) lowPriorityCfc in _questRegistry.LowPriorityCfcQuests)
                    {
                        if (_territoryData.TryGetContentFinderCondition(lowPriorityCfc.ContentFinderConditionId, out TerritoryData.ContentFinderConditionData? cfcData))
                            ImGui.BulletText($"{cfcData.Name}");
                    }
                }
            }
        }
    }

    // The settings store "skip", so a ticked box means the quest is picked up.
    private void DrawPickUp(string label, Func<bool> getSkip, Action<bool> setSkip, string help) =>
        DrawCheckbox(label, () => !getSkip(), value => setSkip(!value), help);

    private void DrawRewardRedemption()
    {
        if (!QstWidgets.SectionHeader(_L("Reward item redemption"), "RewardRedemption", defaultOpen: false))
            return;

        using (ImRaii.PushIndent())
        {
            bool autoRedeemRewardItems = Configuration.Advanced.AutoRedeemRewardItems;
            if (ImGui.Checkbox(_L("Automatically open redeemable items (coffers, minions, emotes, etc.)"),
                    ref autoRedeemRewardItems))
            {
                Configuration.Advanced.AutoRedeemRewardItems = autoRedeemRewardItems;
                Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(_L(
                "After turning in a quest (or before accepting one), Questionable uses items in your inventory that unlock mounts, minions, orchestrion rolls, emotes, and similar rewards. Disable this if you prefer to open them yourself."));

            using (ImRaii.Disabled(!autoRedeemRewardItems))
            {
                using (ImRaii.PushIndent())
                {
                    ImGui.TextWrapped(_L(
                        "Items on this list are never opened automatically. Add venture coffers, Grand Company coffers, or anything else you want to keep closed."));

                    HashSet<uint> blacklist = Configuration.Advanced.AutoRedeemItemBlacklist;
                    _itemBlacklistSelector.ItemSelected = itemId =>
                    {
                        if (blacklist.Add(itemId))
                            Save();
                    };
                    _itemBlacklistSelector.DrawSelection(blacklist);

                    if (blacklist.Count > 0)
                    {
                        using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
                        {
                            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Trash, _L("Clear All")))
                            {
                                blacklist.Clear();
                                Save();
                            }
                        }

                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                            ImGui.SetTooltip(_L("Hold CTRL to enable this button."));

                        ImGui.Separator();
                    }

                    foreach (uint itemId in blacklist.OrderBy(x => x))
                    {
                        using (ImRaii.PushId($"BlacklistItem{itemId}"))
                        {
                            string name = _dataManager.GetExcelSheet<Item>()?.GetRowOrDefault(itemId)?.Name.ToString()
                                          ?? itemId.ToString(CultureInfo.InvariantCulture);
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text($"{name} ({itemId})");

                            using (ImRaii.PushFont(UiBuilder.IconFont))
                            {
                                ImGui.SameLine(ImGui.GetContentRegionAvail().X +
                                               ImGui.GetStyle().WindowPadding.X -
                                               ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X -
                                               ImGui.GetStyle().FramePadding.X * 2);
                            }

                            if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Times))
                                _itemToRemove = itemId;
                        }
                    }

                    if (_itemToRemove is uint removeId)
                    {
                        blacklist.Remove(removeId);
                        _itemToRemove = null;
                        Save();
                    }
                }
            }
        }
    }

    private void DrawUpdates()
    {
        if (!QstWidgets.SectionHeader(_L("Updates"), "Updates", defaultOpen: false))
            return;

        using (ImRaii.PushIndent())
        {
            bool autoUpdatePaths = Configuration.PathData.AutoUpdate;
            if (ImGui.Checkbox(_L("Automatically download quest/gathering path updates"), ref autoUpdatePaths))
            {
                Configuration.PathData.AutoUpdate = autoUpdatePaths;
                Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(_L("Downloads newer quest/gathering paths without needing a full plugin update."));

            if (ImGui.Button(_L("Check for path updates now")))
                _pathDataUpdater.CheckForUpdatesManually();

            ImGui.SameLine();
            ImGui.TextColored(QstTheme.TextMuted, _pathDataUpdater.Status);

            long installedVersion = Configuration.PathData.InstalledDataVersion;
            ImGui.TextColored(QstTheme.TextMuted,
                installedVersion == 0
                    ? _L("Using the path data bundled with the plugin.")
                    : _LF("Downloaded path data version: {0}", installedVersion));
        }
    }
}
