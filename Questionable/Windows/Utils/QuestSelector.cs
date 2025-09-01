using Dalamud.Bindings.ImGui;
using Questionable.Controller;
using Questionable.Model;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Questionable.Windows.Utils;

internal sealed class QuestSelector(QuestRegistry questRegistry)
{
    private string _searchString = string.Empty;

    public Predicate<Quest>? SuggestionPredicate { private get; set; }
    public Predicate<Quest>? DefaultPredicate { private get; set; }
    public Action<Quest>? QuestSelected { private get; set; }

    public void DrawSelection()
    {
        if (QuestSelected == null)
            throw new InvalidOperationException("QuestSelected action must be set before drawing the quest selector.");

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##QuestSelection", "Add Quest...", ImGuiComboFlags.HeightLarge))
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            bool addFirst = ImGui.InputTextWithHint("", "Filter...", ref _searchString, 256,
                ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue);

            IEnumerable<Quest> foundQuests;
            if (!string.IsNullOrEmpty(_searchString))
            {
                bool DefaultPredicate(Quest x) =>
                    x.Info.Name.Contains(_searchString, StringComparison.CurrentCultureIgnoreCase);

                Func<Quest, bool> searchPredicate;
                if (ElementId.TryFromString(_searchString, out ElementId? elementId))
                    searchPredicate = x => DefaultPredicate(x) || x.Id == elementId;
                else
                    searchPredicate = DefaultPredicate;

                foundQuests = questRegistry.AllQuests
                    .Where(x => x.Id is not SatisfactionSupplyNpcId and not AlliedSocietyDailyId)
                    .Where(searchPredicate);
            }
            else
            {
                foundQuests = questRegistry.AllQuests.Where(x => DefaultPredicate?.Invoke(x) ?? true);
            }

            foreach (var quest in foundQuests)
            {
                if (SuggestionPredicate != null && !SuggestionPredicate.Invoke(quest))
                    continue;

                bool addThis = ImGui.Selectable(quest.Info.Name);
                if (addThis || addFirst)
                {
                    QuestSelected(quest);

                    if (addFirst)
                    {
                        ImGui.CloseCurrentPopup();
                        addFirst = false;
                    }
                }
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();
    }
}
