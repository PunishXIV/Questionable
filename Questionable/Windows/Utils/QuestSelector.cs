using Dalamud.Bindings.ImGui;
using Questionable.Model.Questing;
namespace Questionable.Windows.Utils;

[RegisterTransient]
internal sealed class QuestSelector(QuestRegistry questRegistry)
{
    private string _searchString = string.Empty;

    public Predicate<Quest>? SuggestionPredicate { private get; set; }
    public Predicate<Quest>? DefaultPredicate { private get; set; }
    public Action<Quest>? OnSelect { private get; set; }

    public void DrawSelection()
    {
        if (OnSelect == null)
            throw new InvalidOperationException("OnSelect must be set before drawing the quest selector.");

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##QuestSelection", _L("Add Quest..."), ImGuiComboFlags.HeightLarge))
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            bool addFirst = ImGui.InputTextWithHint("##", _L("Filter..."), ref _searchString, 256,
                ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue);

            IEnumerable<Quest> foundQuests;
            if (!string.IsNullOrEmpty(_searchString))
            {
                bool MatchesName(Quest x) => x.Info.Name.Contains(_searchString, StringComparison.CurrentCultureIgnoreCase);

                Func<Quest, bool> matches;
                if (ElementId.TryFromString(_searchString, out ElementId? elementId))
                    matches = x => MatchesName(x) || x.Id == elementId;
                else
                    matches = MatchesName;

                foundQuests = questRegistry.AllQuests
                    .Where(x => x.Id is not SatisfactionSupplyNpcId and not AlliedSocietyDailyId)
                    .Where(matches);
            }
            else
                foundQuests = questRegistry.AllQuests.Where(x => DefaultPredicate?.Invoke(x) ?? true);

            foreach (Quest quest in foundQuests)
            {
                if (SuggestionPredicate != null && !SuggestionPredicate.Invoke(quest))
                    continue;

                bool clicked = ImGui.Selectable(quest.Info.Name);
                if (clicked || addFirst)
                {
                    OnSelect(quest);

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
