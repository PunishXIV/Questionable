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
    IDalamudPluginInterface pluginInterface)
{
    uint _unchecked;
    uint _incomplete;
    public void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Allied Societies"));
        if (!tab)
            return;

        bool debug = configuration.Advanced.Debug;
        bool addPending = debug && DrawTesterTools();

        unsafe
        {
            uint allowances = QuestManager.Instance()->GetBeastTribeAllowance();
            ImGui.Text(_LF("Allowances: {0}/12", allowances));
        }

        if (_incomplete > 0)
        {
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(_LF("Quests marked with yellow have not been completed once yet on this character."),
                                       FontAwesomeIcon.InfoCircle, QstTheme.Amber);
        }

        if (debug && _unchecked > 0)
        {
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(_L("Quests marked with orange need to be reported as working or not via the LastChecked system. Ask Aly for more details!"),
                                       FontAwesomeIcon.InfoCircle, QstTheme.Accent);
            ImGui.SameLine();
            ImGui.Text(_LF("Unchecked: {0}", _unchecked));
        }

        _unchecked = 0;
        _incomplete = 0;

        foreach (EAlliedSociety alliedSociety in alliedSocietyFunctions.GetAlliedSocieties(!configuration.General.HideThingsNotAvailable))
            DrawSociety(alliedSociety, addPending, debug);
    }

    // Path checker tools; true when "Add" was pressed this frame.
    private bool DrawTesterTools()
    {
        bool addPending = false;
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Plus, _L("Add")))
            addPending = true;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Add unchecked quests (from expanded categories) to prio"));
        ImGui.SameLine();

        bool preventQuestCompletion = configuration.Advanced.PreventQuestCompletion;
        bool abandonBeforeComplete = configuration.Advanced.AbandonQuestBeforeCompletion;
        bool removeWhenAbandoned = configuration.Advanced.RemoveFromPriorityWhenAbandoned;
        bool runCommandAfterStop = configuration.Stop.RunCommandAfterStop;
        if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Stop, preventQuestCompletion ? QstTheme.Accent : null))
        {
            configuration.Advanced.PreventQuestCompletion = !preventQuestCompletion;
            pluginInterface.SavePluginConfig(configuration);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Prevent quest completion"));

        ImGui.SameLine();
        if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.SignOutAlt, abandonBeforeComplete ? QstTheme.Accent : null))
        {
            configuration.Advanced.AbandonQuestBeforeCompletion = !abandonBeforeComplete;
            pluginInterface.SavePluginConfig(configuration);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Abandon quest before completion"));

        ImGui.SameLine();
        if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.MinusCircle, removeWhenAbandoned ? QstTheme.Accent : null))
        {
            configuration.Advanced.RemoveFromPriorityWhenAbandoned = !removeWhenAbandoned;
            pluginInterface.SavePluginConfig(configuration);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Remove from priority when abandoned"));

        ImGui.SameLine();
        if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Terminal, runCommandAfterStop ? QstTheme.Accent : null))
        {
            configuration.Stop.RunCommandAfterStop = !runCommandAfterStop;
            pluginInterface.SavePluginConfig(configuration);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Run command when Questionable finishes automatic questing"));

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
        string label = $"{rep}{alliedSociety}###AlliedSociety{(int)alliedSociety}";
        bool isOpen;

        using (ImRaii.Disabled(quests.Count == 0))
        {
            // Ignore Ixal Deliverance and FATE quests when flagging unchecked paths.
            if (debug && quests.Any(x => !x.QuestId.Value.Equals(1569) && ( // is not the Ixal delivery quest "Deliverance", and
                    !questRegistry.TryGetQuest(x.QuestId, out Quest? quest) || // is not a valid quest in the registry, or
                    (quest.Root.Disabled && quest.Root.Comment == null) || // is disabled without a comment explaining why, or
                    (quest.Root.LastChecked.Date != null &&
                        JournalTable.IsStale(quest.Root, DateTime.Now) && // has not been reported checked in more than a week, and
                        !(quest.Root.Comment ?? "").Contains("FATE") // is not a FATE quest, we don't care that much
                    )
                )
            ))
            {
                using (ImRaii.PushColor(ImGuiCol.Text, QstTheme.Accent)) // highlight the category orange
                {
                    isOpen = ImGui.CollapsingHeader(label);
                }
                _unchecked += 1;
            }
            else if (quests.Any(x => !questFunctions.IsQuestComplete(x.QuestId))) // if the character has not completed a quest in this category
            {
                using (ImRaii.PushColor(ImGuiCol.Text, QstTheme.Amber))
                {
                    isOpen = ImGui.CollapsingHeader(label);
                }
            }
            else
            {
                isOpen = ImGui.CollapsingHeader(label);
            }
        }

        questJournalUtils.ShowGroupContextMenu($"DrawAlliedSocietyQuests{alliedSociety}", quests,
            () => GetHighestRankQuests(quests, rank));

        if (!isOpen)
            return;

        using ImRaii.TableDisposable table = journalTable.Begin($"AlliedSociety{(int)alliedSociety}",
            repColumn: neededRep != 0, markStale: debug);
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

    private static List<IQuestInfo> GetHighestRankQuests(List<IQuestInfo> quests, EAlliedSocietyRank currentRank)
    {
        EAlliedSocietyRank? highest = AlliedSocietyQuestFunctions.GetHighestRank(
            quests.Select(x => ((QuestInfo)x).AlliedSocietyRank), currentRank);
        return [.. quests.Where(x => ((QuestInfo)x).AlliedSocietyRank == highest && !x.QuestId.Value.Equals(1569))];
    }

    private void DrawQuest(IQuestInfo questInfo, bool addPending)
    {
        journalTable.QuestRow(questInfo, nameof(AlliedSocietyComponent));
        if (addPending && questRegistry.TryGetQuest(questInfo.QuestId, out Quest? quest) && JournalTable.IsStale(quest.Root, DateTime.Now))
            questController.PriorityManager.Add(questInfo.QuestId);
    }
}
