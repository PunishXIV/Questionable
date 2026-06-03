using System.Collections.Generic;
using ECommons.ExcelServices;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Questing;
namespace Questionable.Validation.Validators;

internal sealed class ClassQuestShouldHaveShortcutValidator : IQuestValidator
{
    private readonly HashSet<ElementId> _classJobQuests = [];

    public ClassQuestShouldHaveShortcutValidator(QuestData questData)
    {
        foreach (Job classJob in typeof(Job).GetEnumValues())
        {
            if (classJob == Job.ADV)
                continue;

            foreach (QuestInfo questInfo in questData.GetClassJobQuests(classJob))
            {
                // TODO maybe remove the level check
                if (questInfo.Level >= 1)
                    _classJobQuests.Add(questInfo.QuestId);
            }
        }
    }

    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        if (!_classJobQuests.Contains(quest.Id))
            yield break;

        bool isTeleportable = false;
        quest.FindSequence(0)?.Steps.ForEach(step =>
        {
            if (step == null || step.IsTeleportableForPriorityQuests())
                isTeleportable = true;
        });
        if (isTeleportable)
            yield break;

        yield return new()
        {
            ElementId = quest.Id,
            Sequence = 0,
            Step = 0,
            Type = EIssueType.ClassQuestWithoutAetheryteShortcut,
            Severity = EIssueSeverity.Error,
            Description = "Class quest should have an aetheryte shortcut to be done automatically"
        };
    }
}
