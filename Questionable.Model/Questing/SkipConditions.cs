namespace Questionable.Model.Questing;

public sealed class SkipConditions
{
    public SkipStepConditions? StepIf { get; set; }
    public SkipAetheryteCondition? AetheryteShortcutIf { get; set; }
    public SkipAetheryteCondition? AethernetShortcutIf { get; set; }
}
