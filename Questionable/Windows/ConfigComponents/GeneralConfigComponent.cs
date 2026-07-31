using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using Lumina.Excel.Sheets;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;
namespace Questionable.Windows.ConfigComponents;

internal sealed class GeneralConfigComponent : ConfigComponent
{
    private static readonly (uint Id, string Name) DefaultMount = (0, _L("Mount Roulette"));
    private static readonly (Job ClassJob, string Name) DefaultClassJob = (Job.ADV, _L("Auto (highest level/item level)"));

    private readonly string[] _grandCompanyNames =
        [_L("None (manually pick quest)"), _L("Maelstrom"), _L("Twin Adder"), _L("Immortal Flames")];

    private readonly QuestRegistry _questRegistry;
    private readonly TerritoryData _territoryData;
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
            { "af",    _L("Afrikaans") + " (WIP)" },
            { "ar",    _L("Arabic") + " (WIP)" },
            { "sq",    _L("Albanian") + " (WIP)" },
            { "eu",    _L("Basque") + " (WIP)" },
            { "be",    _L("Belarusian") + " (WIP)" },
            { "bg",    _L("Bulgarian") + " (WIP)" },
            { "ca",    _L("Catalan") + " (WIP)" },
            { "zh-tw", _L("Chinese (Traditional)") + " (WIP)" },
            { "hr",    _L("Croatian") + " (WIP)" },
            { "cs",    _L("Czech") + " (WIP)" },
            { "en-au", _L("English (Australian)") + " (WIP)" },
            { "fr",    _L("French") + " (WIP)" },
            { "de",    _L("German") + " (WIP)" },
            { "es",    _L("Spanish") + " (WIP)" },
        };
        string language = Configuration.General.Language;
        if (ImGuiComponentsLocal.DrawSearchableCombo(_L("Language"), languages.Keys.ToArray(), languages.Values.ToArray(),
            Configuration.General.Language, ref _langSearchString, ref language))
        {
            var was = Configuration.General.Language;
            Configuration.General.Language = language;
            Save();
            if (was != Configuration.General.Language)
                DalamudInitializer.SetupI18N(Configuration.General.Language);
        }

        if (QstWidgets.SectionHeader(_L("Preferences"), "Preferences", defaultOpen: false))
        {
            ECombatModule combatModule = Configuration.General.CombatModule;
            ImGui.SetNextItemWidth(size.X / 2);
            if (ImGuiEx.EnumCombo(_L("Preferred Combat Module"), ref combatModule))
            {
                Configuration.General.CombatModule = combatModule;
                Save();
            }

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
                if (!StylistIpc.IsInstalled && gearsetSource is EGearsetUpdateSource.Stylist)
                {
                    Svc.Chat.Print(_L("You've set Stylist to manage equipped gear, but it is not installed. Resetting to Vanilla."), CommandHandler.MessageTag, CommandHandler.TagColor);
                    Configuration.General.GearsetUpdateSource = EGearsetUpdateSource.Vanilla;
                    Save();
                }
            }
            ImGui.EndGroup();
            if (!StylistIpc.IsInstalled && ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Stylist is not installed."));

            string chocoboName = Configuration.General.ChocoboName;
            ImGui.SetNextItemWidth(size.X / 2);
            if (ImGui.InputText(_L("Chocobo name"), ref chocoboName, 20))
                Configuration.General.ChocoboName = chocoboName;

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                if (string.IsNullOrWhiteSpace(Configuration.General.ChocoboName))
                    Configuration.General.ChocoboName = _L("Chicken");
                Save();
            }

            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text(_L("The name to give your chocobo during the \"My Little Chocobo\" quest."));
                    ImGui.Text(_L("Defaults to \"Chicken\" if left blank."));
                }
            }

            string displayName = Configuration.General.DisplayName;
            ImGui.SetNextItemWidth(size.X / 2);
            if (ImGui.InputText(_L("Display name"), ref displayName, 20))
                Configuration.General.DisplayName = displayName;

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                if (string.IsNullOrWhiteSpace(Configuration.General.DisplayName))
                    Configuration.General.DisplayName = _L("Anonymous");
                Save();
            }

            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text(_L("The name associated with submissions to help with QST's development."));
                    ImGui.Text(_L("Defaults to \"Anonymous\" if left blank."));
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

                bool hideInAllInstances = Configuration.General.HideInAllInstances;
                if (ImGui.Checkbox(_L("Hide quest window in all instanced duties"), ref hideInAllInstances))
                {
                    Configuration.General.HideInAllInstances = hideInAllInstances;
                    Save();
                }

                bool useEscToCancelQuesting = Configuration.General.UseEscToCancelQuesting;
                if (ImGui.Checkbox(_L("Use ESC to cancel questing/movement"), ref useEscToCancelQuesting))
                {
                    Configuration.General.UseEscToCancelQuesting = useEscToCancelQuesting;
                    Save();
                }

                bool showIncompleteSeasonalEvents = Configuration.General.ShowIncompleteSeasonalEvents;
                if (ImGui.Checkbox(_L("Show details for incomplete seasonal events"), ref showIncompleteSeasonalEvents))
                {
                    Configuration.General.ShowIncompleteSeasonalEvents = showIncompleteSeasonalEvents;
                    Save();
                }

                bool hideSponsorButton = Configuration.General.HideSponsorButton;
                if (ImGui.Checkbox(_L("Hide Sponsor button"), ref hideSponsorButton))
                {
                    Configuration.General.HideSponsorButton = hideSponsorButton;
                    Save();
                }

                bool hideRemainingTasks = Configuration.General.HideRemainingTasks;
                if (ImGui.Checkbox(_L("Hide remaining tasks"), ref hideRemainingTasks))
                {
                    Configuration.General.HideRemainingTasks = hideRemainingTasks;
                    Save();
                }
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
                    if (dontSkipCutscenes)
                    {
                        using (ImRaii.PushIndent(2))
                        {
                            bool dontShowAnswerSuggestions = Configuration.General.DontShowAnswerSuggestions;
                            if (ImGui.Checkbox(_L("and don't show which answer we would have picked for you"), ref dontShowAnswerSuggestions))
                            {
                                Configuration.General.DontShowAnswerSuggestions = dontShowAnswerSuggestions;
                                Save();
                            }
                        }
                    }
                }

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
                        foreach ((uint ContentFinderConditionId, ElementId QuestId, int Sequence) lowPriorityCfc in _questRegistry.LowPriorityContentFinderConditionQuests)
                        {
                            if (_territoryData.TryGetContentFinderCondition(lowPriorityCfc.ContentFinderConditionId, out TerritoryData.ContentFinderConditionData? cfcData))
                                ImGui.BulletText($"{cfcData.Name}");
                        }
                    }
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

                //bool claimMail = Configuration.General.ClaimMail;
                //if (ImGui.Checkbox(_L("Claim mail"), ref claimMail))
                //{
                //    Configuration.General.ClaimMail = claimMail;
                //    Save();
                //}

                //if (ImGui.IsItemHovered())
                //{
                //    using (ImRaii.Tooltip())
                //    {
                //        ImGui.Text(_L("Use MogMail to claim all letters from Delivery Moogles when accepting a quest"));
                //    }
                //}

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
    }
}
