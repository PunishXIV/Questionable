using System.Collections.Generic;
using Questionable.Model.Common.Converter;

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
