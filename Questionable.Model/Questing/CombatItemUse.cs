using Questionable.Model.Questing.Converter;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing;

public sealed class CombatItemUse
{
    public uint ItemId { get; set; }

    [JsonConverter(typeof(CombatItemUseConditionConverter))]
    public ECombatItemUseCondition Condition { get; set; }

    public int Value { get; set; }
}
