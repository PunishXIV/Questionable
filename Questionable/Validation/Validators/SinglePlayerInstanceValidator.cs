using System.Collections.Generic;
using System.Linq;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Questing;
namespace Questionable.Validation.Validators;

internal sealed class SinglePlayerInstanceValidator(TerritoryData territoryData) : IQuestValidator
{
    private readonly Dictionary<ElementId, List<byte>> _questIdToDutyIndexes = territoryData.GetAllQuestsWithQuestBattles()
        .GroupBy(x => x.QuestId)
        .ToDictionary(x => x.Key, x => x.Select(y => y.Index).ToList());

    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        if (_questIdToDutyIndexes.TryGetValue(quest.Id, out List<byte>? indexes))
        {
            foreach (byte index in indexes)
            {
                if (quest.AllSteps().Any(x =>
                    x.Step.InteractionType == EInteractionType.SinglePlayerDuty &&
                    x.Step.SinglePlayerDutyIndex == index))
                {
                    continue;
                }

                yield return new()
                {
                    ElementId = quest.Id,
                    Sequence = null,
                    Step = null,
                    Type = EIssueType.UnusedSinglePlayerInstance,
                    Severity = EIssueSeverity.Error,
                    Description = $"Single player instance {index} not used"
                };
            }
        }
    }
}
