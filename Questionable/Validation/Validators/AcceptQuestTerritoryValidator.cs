// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @alydevs

using System.Collections.Generic;
using System.Linq;
using Questionable.Controller.Steps.Shared;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Model.Questing;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable.Validation.Validators;

/// <summary>
///     For sequence 0 <see cref="EInteractionType.AcceptQuest"/> steps, <c>TerritoryId</c> must either
///     match a territory containing an aetheryte, or the step must teleport in via
///     <c>AetheryteShortcut</c> / <c>AethernetShortcut</c>.
/// </summary>
internal sealed class AcceptQuestTerritoryValidator(IAetheryteTerritoryProvider aetheryteData) : IQuestValidator
{
    private readonly IAetheryteTerritoryProvider _aetheryteData = aetheryteData;
    private static readonly HashSet<uint> CharacterStartingTerritories = [181, 182, 183];

    /// <summary>These territories are handled by custom code in <see cref="Questionable.Controller.Steps.Shared.AetheryteShortcut.CreateAllTasks"/></summary>
    private static readonly HashSet<uint> AetheryteShortcutTerritories = AetheryteShortcut.Territories;

    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        QuestSequence? sequence0 = quest.FindSequence(0);
        if (sequence0 == null)
            yield break;

        HashSet<uint> aetheryteTerritories = [.. _aetheryteData.AetheryteTerritoryIds];

        for (int stepId = 0; stepId < sequence0.Steps.Count; stepId++)
        {
            QuestStep step = sequence0.Steps[stepId];
            if (step.InteractionType != EInteractionType.AcceptQuest)
                continue;

            if (aetheryteTerritories.Contains(step.TerritoryId))
                continue;

            if (step.AetheryteShortcut != null || step.AethernetShortcut != null)
                continue;

            if (sequence0.Steps.Any(x => x.TargetTerritoryId == step.TerritoryId))
                continue;

            if (CharacterStartingTerritories.Contains(step.TerritoryId))
                continue;

            if (AetheryteShortcutTerritories.Contains(step.TerritoryId))
                continue;

            yield return new ValidationIssue
            {
                ElementId = quest.Id,
                Sequence = 0,
                Step = stepId,
                Type = EIssueType.InvalidAcceptQuestTerritory,
                TerritoryId = step.TerritoryId,
                Severity = EIssueSeverity.None,
                Description = _LF("AcceptQuest territory {0} has no aetheryte and no teleport shortcut is set", step.TerritoryId),
            };
        }
    }
}
