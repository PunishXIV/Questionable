namespace Questionable.Windows;

[RegisterSingleton]
internal sealed class QuestSimWindow : LWindow
{
    private readonly QuestController _questController;
    private readonly QuestStarter _questStarter;
    private readonly QuestFunctions _questFunctions;
    private readonly QuestRegistry _questRegistry;
    private readonly IDalamudPluginInterface _pluginInterface;
    private string _search = "";
    private List<Quest> _matches = [];
    private Quest? _quest;

    public QuestSimWindow(
        QuestController questController,
        QuestStarter questStarter,
        QuestFunctions questFunctions,
        QuestRegistry questRegistry,
        IDalamudPluginInterface pluginInterface)
        : base(_L("Quest sim") + "###QuestionableQuestSim")
    {
        _questController = questController;
        _questStarter = questStarter;
        _questFunctions = questFunctions;
        _questRegistry = questRegistry;
        _pluginInterface = pluginInterface;

        Size = new Vector2(700, 450);
        SizeCondition = ImGuiCond.Once;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 200)
        };
    }

    public void Open(Quest? quest)
    {
        _quest = quest ?? _questController.SimulatedQuest?.Quest ?? _questController.CurrentQuest?.Quest;
        IsOpenAndUncollapsed = true;
    }

    public override void DrawContent()
    {
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("###QuestSimSearch", _L("Quest name or ID"), ref _search, 100))
            _matches = Find(_search);

        foreach (Quest match in _matches)
        {
            if (ImGui.Selectable($"{match.Id}  {match.Info.Name}###QuestSimMatch{match.Id}", match == _quest))
            {
                _quest = match;
                _search = "";
                _matches = [];
            }
        }

        if (_quest == null)
            return;

        QuestController.QuestProgress? sim = _questController.SimulatedQuest?.Quest.Id == _quest.Id
            ? _questController.SimulatedQuest
            : null;
        (byte Sequence, int? Step)? here = Here(_quest);

        ImGui.Separator();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"{_quest.Info.Name} ({_quest.Id})");
        if (_questController.SimulatedQuest != null)
        {
            ImGui.SameLine();
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Times, _L("Clear sim")))
                _questStarter.ClearSimulation();
        }

        if (here == null)
            ImGui.TextColored(QstTheme.TextMuted, _L("Not accepted yet: start the sim in sequence 0 to accept it."));

        // A different quest can't take over while QST is busy; its queued tasks would run against the new sim.
        bool canStart = !_questController.HasExternalOperation && (sim != null || !_questController.IsRunning);
        foreach (QuestSequence sequence in _quest.Root.QuestSequence.OrderBy(x => x.Sequence))
        {
            string label = _LF("Sequence {0}", sequence.Sequence);
            if (here?.Sequence == sequence.Sequence)
                label += " - " + _L("you are here");

            bool focus = (sim?.Sequence ?? here?.Sequence) == sequence.Sequence;
            if (!QstWidgets.SectionHeader(label, $"QuestSimSeq{sequence.Sequence}", sequence.Steps.Count, defaultOpen: focus))
                continue;

            DrawSteps(sequence, sim, here, canStart);
        }
    }

    private void DrawSteps(QuestSequence sequence, QuestController.QuestProgress? sim, (byte Sequence, int? Step)? here,
        bool canStart)
    {
        using ImRaii.TableDisposable table = ImRaii.Table($"QuestSimSteps{sequence.Sequence}", 7, ImGuiTableFlags.Borders);
        if (!table)
            return;

        float iconWidth;
        using (_pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            iconWidth = ImGui.CalcTextSize(FontAwesomeIcon.MapMarkerAlt.ToIconString()).X * 2 + ImGui.GetStyle().ItemSpacing.X;

        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, iconWidth);
        ImGui.TableSetupColumn(_L("Step"), ImGuiTableColumnFlags.WidthFixed, 30 * ImGui.GetIO().FontGlobalScale);
        ImGui.TableSetupColumn(_L("Interaction"), ImGuiTableColumnFlags.WidthFixed, 120 * ImGui.GetIO().FontGlobalScale);
        ImGui.TableSetupColumn(_L("Target"), ImGuiTableColumnFlags.WidthFixed, 70 * ImGui.GetIO().FontGlobalScale);
        ImGui.TableSetupColumn(_L("Zone"), ImGuiTableColumnFlags.WidthStretch, 0.4f);
        ImGui.TableSetupColumn(_L("Comment"), ImGuiTableColumnFlags.WidthStretch, 0.6f);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableHeadersRow();

        for (int i = 0; i < sequence.Steps.Count; ++i)
        {
            QuestStep step = sequence.Steps[i];
            using ImRaii.IdDisposable id = ImRaii.PushId(i);
            ImGui.TableNextRow();

            if (ImGui.TableNextColumn())
            {
                ImGui.AlignTextToFramePadding();
                if (here is { Step: { } hereStep } && here.Value.Sequence == sequence.Sequence && hereStep == i)
                    DrawMarker(FontAwesomeIcon.MapMarkerAlt, QstTheme.Info, _L("you are here"));
                if (sim != null && sim.Sequence == sequence.Sequence && sim.Step == i)
                    DrawMarker(FontAwesomeIcon.Flask, QstTheme.Danger, _L("The sim is here"));
            }

            if (ImGui.TableNextColumn())
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(i.ToString(CultureInfo.InvariantCulture));
            }

            if (ImGui.TableNextColumn())
                ImGui.TextUnformatted(step.InteractionType.ToString());

            if (ImGui.TableNextColumn())
                ImGui.TextUnformatted(step.DataId?.ToString(CultureInfo.InvariantCulture) ?? "");

            if (ImGui.TableNextColumn())
                ImGui.TextUnformatted(TerritoryData.GetNameAndId(step.TerritoryId));

            if (ImGui.TableNextColumn())
            {
                using ImRaii.TextWrapDisposable wrap = ImRaii.TextWrapPos(0);
                ImGui.TextUnformatted(step.Comment ?? "");
            }

            if (ImGui.TableNextColumn())
            {
                using (ImRaii.Disabled(!canStart))
                {
                    if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Play))
                        StartHere(sequence.Sequence, i, sim);
                }

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    QstWidgets.Tooltip(_L("Start sim here. Press Play in the Questionable window to run it."));
            }
        }
    }

    private void DrawMarker(FontAwesomeIcon icon, Vector4 color, string tooltip)
    {
        using (_pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            ImGui.TextColored(color, icon.ToIconString());
        if (ImGui.IsItemHovered())
            QstWidgets.Tooltip(tooltip);
        ImGui.SameLine();
    }

    private void StartHere(byte sequence, int step, QuestController.QuestProgress? sim)
    {
        if (sim != null)
            _questController.MoveSimulation(sequence, step);
        else
            _questStarter.SimulateQuest(_quest, sequence, step);
    }

    private (byte Sequence, int? Step)? Here(Quest quest)
    {
        if (_questController.StartedQuest is { } started && started.Quest.Id == quest.Id)
            return (started.Sequence, started.Step);
        if (_questFunctions.IsQuestAccepted(quest.Id) && _questFunctions.GetQuestProgressInfo(quest.Id) is { } progress)
            return (progress.Sequence, null);
        return null;
    }

    private List<Quest> Find(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return [];
        if (ElementId.TryFromString(text, out ElementId? id) && id != null && _questRegistry.TryGetQuest(id, out Quest? quest))
            return [quest];
        return _questRegistry.AllQuests
            .Where(x => x.Info.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(x => x.Info.Name, StringComparer.CurrentCulture)
            .Take(15)
            .ToList();
    }
}
