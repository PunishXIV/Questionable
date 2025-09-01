using Questionable.Model.Common.Converter;
using System.Collections.Generic;

namespace Questionable.Model.Questing.Converter;

public sealed class CombatItemUseConditionConverter() : EnumConverter<ECombatItemUseCondition>(Values)
{
    private static readonly Dictionary<ECombatItemUseCondition, string> Values = new()
    {
        { ECombatItemUseCondition.Incapacitated, "Incapacitated" },
        { ECombatItemUseCondition.HealthPercent, "Health%" },
        { ECombatItemUseCondition.MissingStatus, "MissingStatus" },
    };
}
