using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using Lumina.Excel.Sheets;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;
namespace Questionable.Windows.ConfigComponents;

[RegisterSingleton]
internal sealed class GeneralConfigComponent : ConfigComponent
{
    private static readonly (Job ClassJob, string Name) DefaultClassJob = (Job.ADV, _L("Auto (highest level/item level)"));

    private readonly string[] _grandCompanyNames =
        [_L("Auto"), _L("Maelstrom"), _L("Twin Adder"), _L("Immortal Flames")];

    private readonly QuestRegistry _questRegistry;
    private readonly TerritoryData _territoryData;
    private readonly PathDataUpdater _pathDataUpdater;
    private readonly IDataManager _dataManager;
    private readonly ItemBlacklistSelector _itemBlacklistSelector;
    private readonly PluginConfigComponent _plugins;
    private readonly AutoRetainerIpc _autoRetainer;
    private uint? _itemToRemove;
    private readonly Lazy<List<Job>> _sortedClassJobs;
    private readonly Lazy<(Job[] Ids, string[] Names)> _combatJobs;
    private readonly Lazy<(Job[] Ids, string[] Names)> _craftJobs;
    private readonly Lazy<(Job[] Ids, string[] Names)> _gatherJobs;
    private readonly Lazy<(uint[] Ids, string[] Names)> _bells;
    private string _languageSearch = string.Empty;

    public GeneralConfigComponent(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        IDataManager dataManager,
        ClassJobUtils classJobUtils,
        QuestRegistry questRegistry,
        TerritoryData territoryData,
        PathDataUpdater pathDataUpdater,
        PluginConfigComponent plugins,
        AutoRetainerIpc autoRetainer)
        : base(pluginInterface, configuration)
    {
        _plugins = plugins;
        _autoRetainer = autoRetainer;
        _questRegistry = questRegistry;
        _territoryData = territoryData;
        _pathDataUpdater = pathDataUpdater;
        _dataManager = dataManager;
        _itemBlacklistSelector = new(dataManager);

        _sortedClassJobs = new(() => [.. classJobUtils.SortedJobs.Select(x => x.ClassJob)]);
        _combatJobs = new(() => BuildJobList(
            Enum.GetValues<Job>().Where(x => x != Job.ADV && !x.IsCrafter() && !x.IsGatherer() && !x.IsClass()),
            prependDefault: true));
        _craftJobs = new(() => BuildJobList(
            Enum.GetValues<Job>().Where(x => x != Job.ADV && x.IsCrafter()),
            prependDefault: true));
        _bells = new(() =>
        (
            [SummoningBell.Nearest, SummoningBell.Inn, SummoningBell.Apartment, SummoningBell.PrivateHouse,
                SummoningBell.FcHouse, .. SummoningBell.Bells.Select(x => x.TerritoryId)],
            [_L("Nearest: this zone's, else your grand company's city"), _L("Inn room"), _L("Apartment"), _L("Private house"),
                _L("Free company house"),
                .. SummoningBell.Bells.Select(x => dataManager.GetExcelSheet<TerritoryType>().GetRow(x.TerritoryId).PlaceName.Value.Name.ExtractText())]
        ));
        _gatherJobs = new(() => BuildJobList(
            Enum.GetValues<Job>().Where(x => x == Job.MIN || x == Job.BTN),
            prependDefault: true));
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

    public void DrawGeneral()
    {
        var size = ImGui.GetWindowContentRegionMax();
        using var _ = ImRaii.TextWrapPos(size.X);
        DrawPreferences(size);
        DrawUi();
        DrawUpdates();
    }

    public void DrawQuestingPage()
    {
        using var _ = ImRaii.TextWrapPos(ImGui.GetWindowContentRegionMax().X);
        DrawQuesting();
        DrawRecovery();
        DrawOptionalQuests();
    }

    public void DrawRewardsPage()
    {
        using var _ = ImRaii.TextWrapPos(ImGui.GetWindowContentRegionMax().X);
        DrawBetweenQuests();
        DrawRewardRules();
        DrawRewardRedemption();
    }

    private void DrawPreferences(Vector2 size)
    {
        using QstWidgets.CardScope card = QstWidgets.TitledCard(_L("Preferences"));
        Dictionary<string, string> languages = new(StringComparer.Ordinal){
            { "en",    _L("English") },
            { "ja-jp", _L("Japanese") },
            { "zh-cn", _L("Chinese (Simplified)") },
            { "zh-tw", _L("Chinese (Traditional)") },
            { "ko",    _L("Korean") + " (WIP)" }
        };
        string language = Configuration.General.Language;
        if (ImGuiComponentsLocal.DrawSearchableCombo(_L("Language"), languages.Keys.ToArray(), languages.Values.ToArray(),
            Configuration.General.Language, ref _languageSearch, ref language))
        {
            Configuration.SetLanguage(language);
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

        ImGui.SetNextItemWidth(size.X / 2);
        _plugins.DrawCombatPicker();

        (Job[] combatJobIds, string[] combatJobNames) = _combatJobs.Value;
        ImGui.SetNextItemWidth(size.X / 2);
        DrawComboOption(_L("Preferred combat job"), combatJobIds, combatJobNames,
            () => Configuration.General.CombatJob,
            v => Configuration.General.CombatJob = v);

        ImGui.SetNextItemWidth(size.X / 2);
        DrawComboOption(_L("Arcanist job line"), [Job.SMN, Job.SCH], [_L("Summoner"), _L("Scholar")],
            () => Configuration.General.ArcanistJob,
            v => Configuration.General.ArcanistJob = v);
        ImGuiComponents.HelpMarker(_L("Which job's quests and soul crystal an Arcanist works towards."));

        (Job[] craftJobIds, string[] craftJobNames) = _craftJobs.Value;
        ImGui.SetNextItemWidth(size.X / 2);
        DrawComboOption(_L("Preferred crafting job"), craftJobIds, craftJobNames,
            () => Configuration.General.CraftingJob,
            v => Configuration.General.CraftingJob = v);

        (Job[] gatherJobIds, string[] gatherJobNames) = _gatherJobs.Value;
        ImGui.SetNextItemWidth(size.X / 2);
        DrawComboOption(_L("Preferred gathering job"), gatherJobIds, gatherJobNames,
            () => Configuration.General.GatheringJob,
            v => Configuration.General.GatheringJob = v);

        ImGui.SetNextItemWidth(size.X / 2);
        DrawComboOption(_L("Allied society combat job"),
            [(Job.ADV, false), (Job.ADV, true), .. combatJobIds[1..].Select(x => (x, false))],
            [_L("Same as preferred combat job"), _L("Lowest level that can take the quest"), .. combatJobNames[1..]],
            () => (Configuration.General.SocietyCombatJob, Configuration.General.SocietyLowestJob),
            v => (Configuration.General.SocietyCombatJob, Configuration.General.SocietyLowestJob) = v);
        ImGuiComponents.HelpMarker(_L("Battle society quests switch to this job. 'Lowest level' picks your lowest combat job that can take the quest, to level alt jobs. The next other quest switches back to your preferred combat job."));

        DrawCheckbox(_L("Namazu: do them as a crafter, not a gatherer"),
            () => Configuration.Advanced.NamazuPreferCraft,
            value => Configuration.Advanced.NamazuPreferCraft = value,
            _L("Namazu quests can be done as a crafter or a gatherer; this picks which."));

        ImGui.BeginGroup();
        using (ImRaii.Disabled(!StylistIpc.IsInstalled))
        {
            EGearsetUpdateSource gearsetSource = Configuration.General.GearsetUpdateSource;
            ImGui.SetNextItemWidth(size.X / 2);
            if (ImGuiEx.EnumCombo(_L("Equip gear with") + "###Preferred Gear Upgrade Source", ref gearsetSource))
            {
                Configuration.General.GearsetUpdateSource = gearsetSource;
                Save();
            }
            // A missing Stylist keeps the saved choice; gear steps use Vanilla until it is installed again.
        }
        ImGui.EndGroup();
        if (!StylistIpc.IsInstalled && ImGui.IsItemHovered())
            QstWidgets.Tooltip(_L("Stylist is not installed."));

        string chocoboName = Configuration.General.ChocoboName;
        ImGui.SetNextItemWidth(size.X / 2);
        var randomName = NameGenerator.GenerateFirstName(fast: grandCompany == 0);
        if (ImGui.InputTextEx(_L("Chocobo name"), randomName, ref chocoboName, 20))
            Configuration.General.ChocoboName = chocoboName;

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            if (string.IsNullOrWhiteSpace(Configuration.General.ChocoboName))
                Configuration.General.ChocoboName = randomName;
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

    private void DrawUi()
    {
        using (QstWidgets.TitledCard(_L("UI")))
        {
            bool useQuestionableTheme = Configuration.General.UseQuestionableTheme;
            if (ImGui.Checkbox(_L("Use Questionable theme"), ref useQuestionableTheme))
            {
                Configuration.General.UseQuestionableTheme = useQuestionableTheme;
                Save();
            }

            if (ImGui.IsItemHovered())
            {
                QstWidgets.Tooltip(_L("When disabled, Questionable's windows use your Dalamud style/theme instead."));
            }

            bool showIncompleteEvents = Configuration.General.ShowIncompleteSeasonalEvents;
            if (ImGui.Checkbox(_L("Show seasonal events"), ref showIncompleteEvents))
            {
                Configuration.General.ShowIncompleteSeasonalEvents = showIncompleteEvents;
                Save();
            }

            using (ImRaii.Disabled(!showIncompleteEvents))
            using (ImRaii.PushIndent())
            {
                bool showCompleteEvents = Configuration.General.ShowCompleteSeasonalEvents;
                if (ImGui.Checkbox(_L("Also show finished event quests"), ref showCompleteEvents))
                {
                    Configuration.General.ShowCompleteSeasonalEvents = showCompleteEvents;
                    Save();
                }
            }

            bool hideInAllInstances = Configuration.General.HideInAllInstances;
            if (ImGui.Checkbox(_L("Hide quest window in all instanced duties"), ref hideInAllInstances))
            {
                Configuration.General.HideInAllInstances = hideInAllInstances;
                Save();
            }

            DrawCheckbox(_L("Show window on start"),
                () => Configuration.Advanced.ShowWindowOnStart,
                value => Configuration.Advanced.ShowWindowOnStart = value,
                _L("When enabled, the quest window opens when the plugin loads."));

            DrawCheckbox(_L("Start minimized"),
                () => Configuration.Advanced.StartMinimized,
                value => Configuration.Advanced.StartMinimized = value,
                _L("When enabled, the quest window starts minimized."));
        }
    }

    private void DrawQuesting()
    {
        using (QstWidgets.TitledCard(_L("While questing")))
        {
            bool configureTextAdvance = Configuration.General.ConfigureTextAdvance;
            if (ImGui.Checkbox(_L("Set up TextAdvance for questing"),
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
                    if (ImGui.Checkbox(_L("Don't skip cutscenes or dialogue") + "###but don't skip cutscenes or dialogue", ref dontSkipCutscenes))
                    {
                        Configuration.General.DontSkipCutscenes = dontSkipCutscenes;
                        Save();
                    }
                }
            }
            bool useEscToCancelQuesting = Configuration.General.UseEscToCancelQuesting;
            if (ImGui.Checkbox(_L("Use ESC to cancel questing/movement"), ref useEscToCancelQuesting))
            {
                Configuration.General.UseEscToCancelQuesting = useEscToCancelQuesting;
                Save();
            }

            bool stopOnPlayerMove = Configuration.General.StopOnPlayerMove;
            if (ImGui.Checkbox(_L("Stop when you move your character"), ref stopOnPlayerMove))
            {
                Configuration.General.StopOnPlayerMove = stopOnPlayerMove;
                Save();
            }

            ImGuiComponents.HelpMarker(_L("Pressing a movement key while Questionable runs stops it. Typing in chat doesn't count."));

            bool confirmCurrencyOverCap = Configuration.General.ConfirmCurrencyOverCap;
            if (ImGui.Checkbox(_L("Allow overcap"), ref confirmCurrencyOverCap))
            {
                Configuration.General.ConfirmCurrencyOverCap = confirmCurrencyOverCap;
                Save();
            }

            if (ImGui.IsItemHovered())
            {
                QstWidgets.Tooltip(_L("When a quest reward would take your gil, company seals or tomestones over the cap. Off: Questionable stops so you can spend them. On: it keeps going and the extra is lost."));
            }

            bool sameJobThroughoutQuest = Configuration.General.SameJobThroughoutQuest;
            if (ImGui.Checkbox(_L("Switch back to the job a quest was started with"), ref sameJobThroughoutQuest))
            {
                Configuration.General.SameJobThroughoutQuest = sameJobThroughoutQuest;
                Save();
            }

            bool summonChocobo = Configuration.General.SummonChocobo;
            if (ImGui.Checkbox(_L("Keep your chocobo companion summoned"), ref summonChocobo))
            {
                Configuration.General.SummonChocobo = summonChocobo;
                Save();
            }

            ImGuiComponents.HelpMarker(
                _L("Summons your chocobo companion in the field with Gysahl Greens when it isn't summoned or has under 5 minutes left (needs 'My Feisty Little Chocobo'). Knightshopper buys 99 more when you run out. Pandora's Box auto-summon pauses while Questionable runs."));
        }
    }

    private void DrawBetweenQuests()
    {
        using (QstWidgets.TitledCard(_L("Between quests")))
        {
            bool collectJobGear = Configuration.General.CollectJobGear;
            if (ImGui.Checkbox(_L("Collect free job gear"), ref collectJobGear))
            {
                Configuration.General.CollectJobGear = collectJobGear;
                Save();
            }

            ImGuiComponents.HelpMarker(
                _L("Between quests, fetches the free level 80, 89 and 99 job gear (The Tempest, Old Sharlayan, Tuliyollal) once its shop is unlocked and it beats what you wear, then equips your best gear. Once per character and job."));

            bool repairGear = Configuration.General.RepairGear;
            if (ImGui.Checkbox(_L("Repair gear"), ref repairGear))
            {
                Configuration.General.RepairGear = repairGear;
                Save();
            }

            ImGuiComponents.HelpMarker(
                _L("Between quests, Questionable repairs gear below the set condition: with Dark Matter when one of your crafters can, otherwise at your Grand Company's mender (or your starting city's) for gil."));

            using (ImRaii.Disabled(!repairGear))
            using (ImRaii.PushIndent())
            {
                int repairBelow = Math.Clamp(Configuration.General.RepairBelowPercent, 1, 99);
                ImGui.SetNextItemWidth(150f);
                if (ImGui.SliderInt(_L("Repair below condition (%)"), ref repairBelow, 1, 99))
                {
                    Configuration.General.RepairBelowPercent = repairBelow;
                    Save();
                }
            }

            bool claimMail = Configuration.General.ClaimMail;
            if (ImGui.Checkbox(_L("Claim Mog Station and reward mail"), ref claimMail))
            {
                Configuration.General.ClaimMail = claimMail;
                Save();
            }

            ImGuiComponents.HelpMarker(
                _L("Between quests, when a delivery moogle is nearby or you're in Limsa Lominsa, Gridania or Ul'dah, walks to it and has Mogmail claim waiting Mog Station and reward mail. Never teleports just for mail. Mogmail's Auto Pop decides whether items get used. Needs Mogmail."));

            bool runNoviceHall = Configuration.General.RunNoviceHall;
            if (ImGui.Checkbox(_L("Run the Hall of the Novice for the Neophyte's Ring"), ref runNoviceHall))
            {
                Configuration.General.RunNoviceHall = runNoviceHall;
                Save();
            }

            ImGuiComponents.HelpMarker(
                _L("Between quests, at class level 49-59 without the Neophyte's Ring, AutoDuty runs the three new Hall of the Novice exercises. The ring gives 30% more EXP up to level 60. Needs AutoDuty with duties on."));
        }
    }

    public void DrawRetainersPage()
    {
        using var _ = ImRaii.TextWrapPos(ImGui.GetWindowContentRegionMax().X);
        using QstWidgets.CardScope card = QstWidgets.TitledCard(_L("AutoRetainer"));
        using var disabled = ImRaii.Disabled(!_autoRetainer.IsReady());
        Configuration.GeneralConfiguration general = Configuration.General;
        float width = ImGui.GetWindowContentRegionMax().X / 2;
        string charactersLabel = _L("Characters"), bellLabel = _L("Summoning bell");
        float labelWidth = Math.Max(ImGui.CalcTextSize(charactersLabel).X, ImGui.CalcTextSize(bellLabel).X) +
                           ImGui.GetStyle().ItemSpacing.X * 2;

        DrawCheckbox(_L("Send out between quests"), () => general.SendRetainers, value => general.SendRetainers = value);
        // Each works alone: one pass at the start, then questing without AutoRetainer.
        DrawCheckbox(_L("Send out when you start questing"), () => general.RetainersOnStart,
            value => general.RetainersOnStart = value);

        using (ImRaii.Disabled(!general.SendRetainers && !general.RetainersOnStart))
        {
            DrawLabel(charactersLabel, labelWidth);
            ImGui.SetNextItemWidth(width);
            DrawComboOption("##Characters", [false, true],
                [_L("All AutoRetainer characters (multi-mode)"), _L("Only this character, at a summoning bell")],
                () => general.RetainersAtBell, value => general.RetainersAtBell = value);
            ImGuiComponents.HelpMarker(general.RetainersAtBell
                ? _L("Questionable goes to the summoning bell you pick and AutoRetainer sends this character's retainers there, then switches AutoRetainer off again. No character switching. Inn and housing bells need Lifestream.")
                : _L("AutoRetainer does one multi-mode pass over all its multi-mode characters, then Questionable logs back in to this character and keeps questing. Needs Lifestream."));

            DrawCheckbox(_L("Retainers"), () => general.SendVentures, value => general.SendVentures = value);
            ImGui.SameLine();
            using (ImRaii.Disabled(general.RetainersAtBell))
                DrawCheckbox(_L("Submersibles"), () => general.SendSubmersibles, value => general.SendSubmersibles = value);
            if (general.RetainersAtBell && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                QstWidgets.Tooltip(_L("Submersibles go through AutoRetainer's multi-mode only."));

            if (general.RetainersAtBell)
            {
                (uint[] ids, string[] names) = _bells.Value;
                DrawLabel(bellLabel, labelWidth);
                ImGui.SetNextItemWidth(width);
                DrawComboOption("##SummoningBell", ids, names, () => general.RetainerBell,
                    value => general.RetainerBell = value);
            }
        }
    }

    private static void DrawLabel(string label, float width)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine(width);
    }

    private void DrawRecovery()
    {
        using (QstWidgets.TitledCard(_L("Recovery and reports")))
        {
            bool autoStepRefreshEnabled = Configuration.General.AutoStepRefreshEnabled;
            if (ImGui.Checkbox(_L("Retry the current step when stuck"), ref autoStepRefreshEnabled))
            {
                Configuration.General.AutoStepRefreshEnabled = autoStepRefreshEnabled;
                Save();
            }

            ImGuiComponents.HelpMarker(_L("Retries movement and simple interactions (talking, attuning, emotes) when nothing has changed for the set time. Combat, cutscenes, dialogue and duties pause the timer."));

            using (ImRaii.Disabled(!autoStepRefreshEnabled))
            using (ImRaii.PushIndent())
            {
                int autoStepRefreshDelay = Math.Clamp(Configuration.General.AutoStepRefreshDelaySeconds, 30, 180);
                ImGui.SetNextItemWidth(150f);
                if (ImGui.SliderInt(_L("Retry after (seconds)"), ref autoStepRefreshDelay, 30, 180))
                {
                    Configuration.General.AutoStepRefreshDelaySeconds = autoStepRefreshDelay;
                    Save();
                }

                int autoStepRefreshMaxTries = Configuration.General.AutoStepRefreshMaxTries;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.SliderInt(_L("Retries before stopping"), ref autoStepRefreshMaxTries, 1, 10))
                {
                    Configuration.General.AutoStepRefreshMaxTries = autoStepRefreshMaxTries;
                    Save();
                }
            }

            int deathLimit = Math.Clamp(Configuration.General.DeathLimit, 1, 10);
            ImGui.SetNextItemWidth(150f);
            if (ImGui.SliderInt(_L("Deaths on one step before stopping"), ref deathLimit, 1, 10))
            {
                Configuration.General.DeathLimit = deathLimit;
                Save();
            }

            ImGuiComponents.HelpMarker(_L("After a death Questionable returns and retries the step; it stops once you die this many times on the same step."));

            bool shareResults = Configuration.Reports.Share == true;
            if (ImGui.Checkbox(_L("Share quest results to help fix paths"), ref shareResults))
            {
                Configuration.Reports.Share = shareResults;
                Save();
            }

            ImGuiComponents.HelpMarker(_L("After each quest Questionable runs, it sends whether the path worked. If it stopped, also the step, what went wrong, plugin versions and its last ten actions. When a quest errors, it also uploads the troubleshooting zip (Questionable's logs and settings, with your character names removed), kept for 30 days. Never your account or IP address. Reports are kept for 180 days."));
        }
    }

    private void DrawOptionalQuests()
    {
        using (QstWidgets.TitledCard(_L("Optional quests")))
        {
            DrawPickUp(_L("Pick up aether currents and aether current quests"),
                () => Configuration.Advanced.SkipAetherCurrents,
                value => Configuration.Advanced.SkipAetherCurrents = value,
                _L("When off, you pick up aether currents yourself, or later with an aether current preset in Priority Quests."));

            DrawPickUp(_L("Pick up class/job/role quests"),
                () => Configuration.Advanced.SkipClassJobQuests,
                value => Configuration.Advanced.SkipClassJobQuests = value,
                _L("ARR, Heavensward and Stormblood (level 70) skills come from these quests. Not recommended if you queue with the Duty Finder or Party Finder."));

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
                _L("'My Feisty Little Chocobo' lets your chocobo companion fight with you in the field. When ticked, it is done automatically after 'My Little Chocobo'."));

            bool skipLowPriorityInstances = Configuration.General.SkipLowPriorityDuties;
            if (ImGui.Checkbox(_L("Don't wait on optional dungeons and raids"), ref skipLowPriorityInstances))
            {
                Configuration.General.SkipLowPriorityDuties = skipLowPriorityInstances;
                Save();
            }

            ImGuiComponentsLocal.HelpMarker(
                _L("Some optional quests (aether currents, ARR alliance raids) end in a duty. When ticked, Questionable moves on to other quests instead of waiting for you to run it.") +
                "\n\n" + _L("This affects the following dungeons and raids:"),
                [.. _questRegistry.LowPriorityCfcQuests
                    .Select(x => _territoryData.TryGetCfc(x.CfcId, out TerritoryData.CfcData? cfc) ? cfc.Name : null)
                    .OfType<string>()]);
        }
    }

    private void DrawRewardRules()
    {
        using QstWidgets.CardScope card = QstWidgets.TitledCard(_L("Quest reward choice"));
        using (ImRaii.PushColor(ImGuiCol.Text, QstTheme.TextMuted))
            ImGui.TextWrapped(_L("The first ticked rule that matches picks the reward; otherwise the highest vendor value. Needs \"Set up TextAdvance for questing\" on the Questing page."));

        List<Configuration.ERewardRule> order = Configuration.General.RewardOrder
            .Concat(RewardPickHandler.DefaultOrder).Distinct().ToList();
        for (int i = 0; i < order.Count; i++)
        {
            Configuration.ERewardRule rule = order[i];
            using ImRaii.IdDisposable id = ImRaii.PushId((int)rule);
            using (ImRaii.Disabled(i == 0))
            {
                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.ArrowUp))
                    MoveRewardRule(order, i, i - 1);
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(i == order.Count - 1))
            {
                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.ArrowDown))
                    MoveRewardRule(order, i, i + 1);
            }

            ImGui.SameLine();
            bool enabled = !Configuration.General.RewardRulesOff.Contains(rule);
            if (ImGui.Checkbox(RewardRuleLabel(rule), ref enabled))
            {
                if (enabled)
                    Configuration.General.RewardRulesOff.Remove(rule);
                else
                    Configuration.General.RewardRulesOff.Add(rule);
                Save();
            }
        }
    }

    private void MoveRewardRule(List<Configuration.ERewardRule> order, int from, int to)
    {
        (order[from], order[to]) = (order[to], order[from]);
        Configuration.General.RewardOrder = order;
        Save();
    }

    private static string RewardRuleLabel(Configuration.ERewardRule rule) => rule switch
    {
        Configuration.ERewardRule.UnownedCollectible => _L("Unowned collectible (mount, minion, orchestrion roll, card, fashion accessory)"),
        Configuration.ERewardRule.GearUpgrade => _L("Gear upgrade for your current job"),
        Configuration.ERewardRule.Coffer => _L("Gear coffer for your current job"),
        Configuration.ERewardRule.AllaganPieces => _L("Allagan pieces (sold for gil)"),
        _ => _L("Highest vendor value"),
    };

    // The settings store "skip", so a ticked box means the quest is picked up.
    private void DrawPickUp(string label, Func<bool> getSkip, Action<bool> setSkip, string help) =>
        DrawCheckbox(label, () => !getSkip(), value => setSkip(!value), help);

    private void DrawRewardRedemption()
    {
        using (QstWidgets.TitledCard(_L("Reward item redemption")))
        {
            bool autoRedeemRewardItems = Configuration.Advanced.AutoRedeemRewardItems;
            if (ImGui.Checkbox(_L("Automatically open redeemable items (coffers, minions, emotes, etc.)"),
                    ref autoRedeemRewardItems))
            {
                Configuration.Advanced.AutoRedeemRewardItems = autoRedeemRewardItems;
                Save();
            }

            ImGuiComponents.HelpMarker(_L(
                "After turning in a quest (or before accepting one), uses items that unlock mounts, minions, orchestrion rolls, emotes and similar. Turn off to open them yourself."));

            using (ImRaii.Disabled(!autoRedeemRewardItems))
            {
                using (ImRaii.PushIndent())
                {
                    ImGui.TextWrapped(_L(
                        "Items on this list are never opened automatically. Add venture coffers, Grand Company coffers, or anything else you want to keep closed."));

                    HashSet<uint> blacklist = Configuration.Advanced.AutoRedeemItemBlacklist;
                    _itemBlacklistSelector.OnSelect = itemId =>
                    {
                        if (blacklist.Add(itemId))
                            Save();
                    };
                    _itemBlacklistSelector.DrawSelection(blacklist);

                    if (blacklist.Count > 0)
                    {
                        using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
                        {
                            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Trash, _L("Clear all")))
                            {
                                blacklist.Clear();
                                Save();
                            }
                        }

                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                            QstWidgets.Tooltip(_L("Hold CTRL to enable this button."));

                        ImGui.Separator();
                    }

                    foreach (uint itemId in blacklist.OrderBy(x => x))
                    {
                        using (ImRaii.PushId($"BlacklistItem{itemId}"))
                        {
                            string name = _dataManager.GetExcelSheet<Item>()?.GetRowOrDefault(itemId)?.Name.ToString(null, CultureInfo.CurrentCulture)
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
        using (QstWidgets.TitledCard(_L("Updates")))
        {
            bool autoUpdatePaths = Configuration.PathData.AutoUpdate;
            if (ImGui.Checkbox(_L("Automatically download quest/gathering path updates"), ref autoUpdatePaths))
            {
                Configuration.PathData.AutoUpdate = autoUpdatePaths;
                Save();
            }

            ImGuiComponents.HelpMarker(_L("Downloads newer quest/gathering paths without needing a full plugin update."));

            bool showCounts = Configuration.Reports.ShowCounts;
            if (ImGui.Checkbox(_L("Show community quest results"), ref showCounts))
            {
                Configuration.Reports.ShowCounts = showCounts;
                Save();
            }

            ImGuiComponents.HelpMarker(_L("Shows how often each quest's path worked for others in the last 30 days: green works, orange needs more reports, red has problems, grey has no data. Sends nothing about you."));

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
