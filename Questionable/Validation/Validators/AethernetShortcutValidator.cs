using Questionable.Model.Common;
using Questionable.Model.Questing;
namespace Questionable.Validation.Validators;

internal sealed class AethernetShortcutValidator(AetheryteData aetheryteData) : IQuestValidator
{
    private readonly AetheryteData _aetheryteData = aetheryteData;

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
            return new()
            {
                ElementId = elementId,
                Sequence = (byte)sequenceNo,
                Step = stepId,
                Type = EIssueType.InvalidAethernetShortcut,
                Severity = EIssueSeverity.Error,
                Description = _LF("Invalid aethernet shortcut: {0} to {1}", aethernetShortcut.From, aethernetShortcut.To)
            };
        }

        return null;
    }
}
