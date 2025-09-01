using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using System.Collections.Generic;
using System.Linq;

namespace Questionable.Validation.Validators;

internal sealed class AethernetShortcutValidator : IQuestValidator
{
    private readonly AetheryteData _aetheryteData;

    public AethernetShortcutValidator(AetheryteData aetheryteData)
    {
        _aetheryteData = aetheryteData;
    }

    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        return quest.AllSteps()
            .Select(x => Validate(quest.Id, x.Sequence.Sequence, x.StepId, x.Step.AethernetShortcut))
            .Where(x => x != null)
            .Cast<ValidationIssue>();
    }

    private ValidationIssue? Validate(ElementId elementId, int sequenceNo, int stepId, AethernetShortcut? aethernetShortcut)
    {
        if (aethernetShortcut == null)
            return null;

        ushort fromGroup = _aetheryteData.AethernetGroups.GetValueOrDefault(aethernetShortcut.From);
        ushort toGroup = _aetheryteData.AethernetGroups.GetValueOrDefault(aethernetShortcut.To);
        if (fromGroup != toGroup)
        {
            return new ValidationIssue
            {
                ElementId = elementId,
                Sequence = (byte)sequenceNo,
                Step = stepId,
                Type = EIssueType.InvalidAethernetShortcut,
                Severity = EIssueSeverity.Error,
                Description = $"Invalid aethernet shortcut: {aethernetShortcut.From} to {aethernetShortcut.To}"
            };
        }

        return null;
    }
}
