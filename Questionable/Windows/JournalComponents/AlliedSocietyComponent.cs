using FFXIVClientStructs.FFXIV.Client.Game;
namespace Questionable.Windows.JournalComponents;

[RegisterSingleton]
internal sealed class AlliedSocietyComponent
(
    QuestFunctions questFunctions,
    QuestController questController,
    AlliedSocietyQuestFunctions alliedSocietyFunctions,
    QuestData questData,
    QuestRegistry questRegistry,
    QuestJournalUtils questJournalUtils,
    JournalTable journalTable,
    Configuration configuration,
    DailiesController dailiesController,
    QuestReporter questReporter,
    IDalamudPluginInterface pluginInterface)
{
    private readonly Dictionary<EAlliedSociety, EType?> _types = [];
    private readonly DragOrder<EAlliedSociety> _dailiesDrag = new();
    uint _needsAttention;
    uint _incomplete;

    internal enum EType
    {
        Combat,
        Crafting,
        Gathering,
        CraftingAndGathering,
    }
    public void Draw()
    {
        bool debug = configuration.Advanced.Debug;
        bool addPending = debug && DrawTesterTools();

        unsafe
        {
            uint allowances = QuestManager.Instance()->GetBeastTribeAllowance();
            ImGui.Text(_LF("Allowances: {0}/12", allowances));
        }

        if (_incomplete > 0)
        {
            ImGuiComponents.HelpMarker(_L("Quests marked with yellow have not been completed once yet on this character."),
                                       FontAwesomeIcon.InfoCircle, QstTheme.Amber);
        }

        if (debug && _needsAttention > 0)
        {
            ImGuiComponents.HelpMarker(_L("Societies needing tests: reported issues, old or insufficient reports, unreported paths, or missing paths. Hover a society for details. Unavailable reports do not add quests to the testing queue."),
                                       FontAwesomeIcon.InfoCircle, QstTheme.Accent);
            ImGui.SameLine();
            ImGui.Text(_LF("Needs attention: {0}", _needsAttention));
        }

        _needsAttention = 0;
        _incomplete = 0;

        DrawDailies();

        foreach (EAlliedSociety alliedSociety in alliedSocietyFunctions.GetAlliedSocieties())
            DrawSociety(alliedSociety, addPending, debug);
    }

    private void DrawDailies()
    {
        List<EAlliedSociety> societies = configuration.Dailies.Societies;
        using (ImRaii.Disabled(societies.Count == 0 || !dailiesController.CanStart))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Play, _L("Do dailies")))
                dailiesController.Start();
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            QstWidgets.Tooltip(societies.Count == 0
                ? _L("Tick the societies to include with the box in front of their name.")
                : _L("Does today's dailies for the ticked societies. Higher on the list gets your allowances first; unlocks and rank-ups go first."));

        using var disabled = ImRaii.Disabled(questController.IsRunning);
        for (int i = 0; i < societies.Count; i++)
        {
            _dailiesDrag.Handle(societies[i], enabled: societies.Count > 1);
            ImGui.SameLine();
            ImGui.Text($"{i + 1}. {societies[i]}");
        }

        if (_dailiesDrag.End(societies))
            pluginInterface.SavePluginConfig(configuration);
    }

    internal static Vector4? HeaderColor(bool debug, bool showReports, bool missing, EReportState state, bool incomplete) =>
        debug && missing && state != EReportState.Problems ? QstTheme.Accent :
        showReports && state != EReportState.Unknown ? ReportUi.Color(state) :
        incomplete ? QstTheme.Amber : null;

    private void DrawDailyCheckbox(EAlliedSociety alliedSociety)
    {
        using var disabled = ImRaii.Disabled(questController.IsRunning);
        List<EAlliedSociety> societies = configuration.Dailies.Societies;
        bool ticked = societies.Contains(alliedSociety);
        if (ImGui.Checkbox($"##Dailies{(int)alliedSociety}", ref ticked))
        {
            if (ticked)
                societies.Add(alliedSociety);
            else
                societies.RemoveAll(x => x == alliedSociety);
            pluginInterface.SavePluginConfig(configuration);
        }

        if (ImGui.IsItemHovered())
            QstWidgets.Tooltip(_L("Include in Do dailies"));
        ImGui.SameLine();
    }

    private bool DrawTesterTools()
    {
        bool addPending = false;
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Plus, _L("Add for testing")))
            addPending = true;
        if (ImGui.IsItemHovered())
            QstWidgets.Tooltip(_L("Adds quests from expanded categories that need testing to Priority Quests, including unreported paths once reports have loaded."));
        ImGui.SameLine();

        bool preventQuestCompletion = configuration.Advanced.PreventQuestCompletion;
        bool abandonBeforeComplete = configuration.Advanced.AbandonQuestBeforeCompletion;
        bool removeWhenAbandoned = configuration.Advanced.RemoveFromPriorityWhenAbandoned;
        bool runCommandAfterStop = configuration.Stop.RunCommandAfterStop;
        using (QstWidgets.SegmentGroup(preventQuestCompletion || runCommandAfterStop))
        {
            if (QstWidgets.SegmentToggle(FontAwesomeIcon.Stop, preventQuestCompletion,
                    _L("Prevent quest completion: on"), _L("Prevent quest completion: off")))
            {
                configuration.Advanced.PreventQuestCompletion = !preventQuestCompletion;
                pluginInterface.SavePluginConfig(configuration);
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(!preventQuestCompletion))
            {
                if (QstWidgets.SegmentToggle(FontAwesomeIcon.SignOutAlt, abandonBeforeComplete,
                        _L("Abandon quest before completion: on"), _L("Abandon quest before completion: off")))
                {
                    configuration.Advanced.AbandonQuestBeforeCompletion = !abandonBeforeComplete;
                    pluginInterface.SavePluginConfig(configuration);
                }
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(!preventQuestCompletion || !abandonBeforeComplete))
            {
                if (QstWidgets.SegmentToggle(FontAwesomeIcon.MinusCircle, removeWhenAbandoned,
                        _L("Remove from Priority Quests when abandoned: on"), _L("Remove from Priority Quests when abandoned: off")))
                {
                    configuration.Advanced.RemoveFromPriorityWhenAbandoned = !removeWhenAbandoned;
                    pluginInterface.SavePluginConfig(configuration);
                }
            }

            ImGui.SameLine();
            if (QstWidgets.SegmentToggle(FontAwesomeIcon.Terminal, runCommandAfterStop,
                    _L("Run commands (Stopping page): on"), _L("Run commands (Stopping page): off")))
            {
                configuration.Stop.RunCommandAfterStop = !runCommandAfterStop;
                pluginInterface.SavePluginConfig(configuration);
            }
        }

        ImGui.SameLine();
        return addPending;
    }

    private void DrawSociety(EAlliedSociety alliedSociety, bool addPending, bool debug)
    {
        List<IQuestInfo> quests = alliedSocietyFunctions.GetAvailableQuests(alliedSociety)
            .Select(x => questData.GetQuestInfo(x))
            .ToList();
        (EAlliedSocietyRank rank, ushort currentRep, ushort neededRep) = questFunctions.GetRankAndReputation(alliedSociety);

        string rep = neededRep != 0 ? $"({rank} {currentRep}/{neededRep}) " : "";
        string type = TypeName(alliedSociety) is { } typeName ? $" - {typeName}" : "";
        string label = string.Create(CultureInfo.CurrentCulture, $"{rep}{alliedSociety}{type}###AlliedSociety{(int)alliedSociety}");
        bool isOpen;

        List<(string Name, ReportStatus Status)> reports = [];
        List<string> missing = [];
        foreach (IQuestInfo info in quests.Where(x => x.QuestId.Value != 1569))
        {
            if (!questRegistry.TryGetQuest(info.QuestId, out Quest? path) ||
                (path.Root.Disabled && path.Root.Comment == null))
                missing.Add(info.Name);
            else if (!path.Root.Disabled && !(path.Root.Comment ?? "").Contains("FATE", StringComparison.Ordinal))
                reports.Add((info.Name, questReporter.StatusFor(path)));
        }
        ReportStatus group = ReportStatus.Worst(reports.Select(x => x.Status));
        if (missing.Count > 0 || reports.Any(x => x.Status.NeedsAttention))
            _needsAttention++;

        bool incomplete = quests.Any(x => !questFunctions.IsQuestComplete(x.QuestId));
        if (incomplete)
            _incomplete++;
        Vector4? color = HeaderColor(debug, configuration.Reports.ShowCounts, missing.Count > 0, group.State, incomplete);

        DrawDailyCheckbox(alliedSociety);
        using (ImRaii.Disabled(quests.Count == 0))
        {
            ImRaii.ColorDisposable? push = color is { } c ? ImRaii.PushColor(ImGuiCol.Text, c) : null;
            isOpen = ImGui.CollapsingHeader(label);
            push?.Dispose();
        }

        if (ImGui.IsItemHovered() && (debug ? reports.Count > 0 || missing.Count > 0 : configuration.Reports.ShowCounts && group.State != EReportState.Unknown))
        {
            string details = ReportUi.GroupDescription(reports);
            if (debug && missing.Count > 0)
                details += "\n" + _LF("Missing or disabled paths: {0}", string.Join(", ", missing));
            QstWidgets.Tooltip(details);
        }

        questJournalUtils.ShowGroupContextMenu($"DrawAlliedSocietyQuests{alliedSociety}", quests,
            () => GetHighestRankQuests(quests, rank));

        if (!isOpen)
            return;

        using ImRaii.TableDisposable table = journalTable.Begin(string.Create(CultureInfo.InvariantCulture, $"AlliedSociety{(int)alliedSociety}"),
            repColumn: neededRep != 0);
        if (!table)
            return;

        if (alliedSociety <= EAlliedSociety.Ixal)
        {
            for (byte i = 1; i <= 8; ++i)
            {
                List<IQuestInfo> questsByRank = quests.Where(quest => (byte)((QuestInfo)quest).AlliedSocietyRank == i && !quest.QuestId.Value.Equals(1569)).ToList();
                if (questsByRank.Count == 0)
                    continue;

                JournalTable.BeginRow();
                ImGui.Text($"{(EAlliedSocietyRank)i}");
                questJournalUtils.ShowGroupContextMenu($"DrawAlliedSocietyQuests{alliedSociety}/{(EAlliedSocietyRank)i}", questsByRank);
                foreach (IQuestInfo quest in questsByRank)
                    DrawQuest(quest, addPending);
            }
        }
        else
        {
            foreach (IQuestInfo quest in quests)
                DrawQuest(quest, addPending);
        }
    }

    private string? TypeName(EAlliedSociety society)
    {
        if (!_types.TryGetValue(society, out EType? type))
        {
            List<QuestInfo> quests = questData.GetAllByAlliedSociety(society);
            List<QuestInfo> dailies = quests.Where(x => x.IsRepeatable).ToList();
            type = GetSocietyType((dailies.Count > 0 ? dailies : quests)
                .Where(x => x.ClassJobs.Count > 0)
                .Select(x => (x.ClassJobs.Any(y => !y.IsCrafter() && !y.IsGatherer()),
                    x.ClassJobs.Any(y => y.IsCrafter()), x.ClassJobs.Any(y => y.IsGatherer()))));
            _types[society] = type;
        }

        return type switch
        {
            EType.Combat => _L("Combat"),
            EType.Crafting => _L("Crafting"),
            EType.Gathering => _L("Gathering"),
            EType.CraftingAndGathering => _L("Crafting & Gathering"),
            _ => null,
        };
    }

    internal static EType? GetSocietyType(IEnumerable<(bool Combat, bool Crafter, bool Gatherer)> quests)
    {
        int combat = 0, crafting = 0, gathering = 0, mixed = 0;
        foreach ((bool anyCombat, bool crafter, bool gatherer) in quests)
        {
            if (anyCombat)
                ++combat;
            else if (crafter && gatherer)
                ++mixed;
            else if (crafter)
                ++crafting;
            else if (gatherer)
                ++gathering;
        }

        if (combat + crafting + gathering + mixed == 0)
            return null;
        if (combat >= crafting + gathering + mixed)
            return EType.Combat;
        if (mixed > 0 || (crafting > 0 && gathering > 0))
            return EType.CraftingAndGathering;
        return crafting > 0 ? EType.Crafting : EType.Gathering;
    }

    private static List<IQuestInfo> GetHighestRankQuests(List<IQuestInfo> quests, EAlliedSocietyRank currentRank)
    {
        EAlliedSocietyRank? highest = AlliedSocietyQuestFunctions.GetHighestRank(
            quests.Select(x => ((QuestInfo)x).AlliedSocietyRank), currentRank);
        return [.. quests.Where(x => ((QuestInfo)x).AlliedSocietyRank == highest && !x.QuestId.Value.Equals(1569))];
    }

    private void DrawQuest(IQuestInfo questInfo, bool addPending)
    {
        journalTable.QuestRow(questInfo, nameof(AlliedSocietyComponent));
        if (addPending && questRegistry.TryGetQuest(questInfo.QuestId, out Quest? quest) &&
            questReporter.StatusFor(quest).NeedsAttention)
            questController.PriorityManager.Add(questInfo.QuestId);
    }
}
