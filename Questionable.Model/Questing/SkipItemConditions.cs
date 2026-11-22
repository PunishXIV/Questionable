namespace Questionable.Model.Questing;

public sealed class SkipItemConditions
{
    public bool NotInInventory { get; set; }

    public override string ToString() => $"{nameof(NotInInventory)}: {NotInInventory}";
}
