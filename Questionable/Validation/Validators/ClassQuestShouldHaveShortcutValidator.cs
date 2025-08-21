using System.Collections.Generic;
using LLib.GameData;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Validation.Validators;

internal sealed class ClassQuestShouldHaveShortcutValidator : IQuestValidator
{
    private readonly HashSet<ElementId> _classJobQuests = [];

    public ClassQuestShouldHaveShortcutValidator(QuestData questData)
    {
        foreach (EClassJob classJob in typeof(EClassJob).GetEnumValues())
        {
            if (classJob == EClassJob.Adventurer)
                continue;

            foreach (var questInfo in questData.GetClassJobQuests(classJob))
            {
                // TODO maybe remove the level check
                if (questInfo.Level > 1)
                    _classJobQuests.Add(questInfo.QuestId);
            }
        }
    }

    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        if (!_classJobQuests.Contains(quest.Id))
            yield break;

        var firstStep = quest.FindSequence(0)?.FindStep(0);
        if (firstStep == null)
            yield break;

        if (firstStep.IsTeleportableForPriorityQuests())
            yield break;

        yield return new ValidationIssue
        {
            ElementId = quest.Id,
            Sequence = 0,
            Step = 0,
            Type = EIssueType.ClassQuestWithoutAetheryteShortcut,
            Severity = EIssueSeverity.Error,
            Description = "Class quest should have an aetheryte shortcut to be done automatically",
        };
    }
}
