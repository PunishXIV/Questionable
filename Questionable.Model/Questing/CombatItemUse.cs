using System.Text.Json.Serialization;
using Questionable.Model.Questing.Converter;

namespace Questionable.Model.Questing;

public sealed class CombatItemUse
{
    public uint ItemId { get; set; }

    [JsonConverter(typeof(CombatItemUseConditionConverter))]
    public ECombatItemUseCondition Condition { get; set; }

    public int Value { get; set; }
}
