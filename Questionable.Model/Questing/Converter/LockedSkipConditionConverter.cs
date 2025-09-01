using Questionable.Model.Common.Converter;
using System.Collections.Generic;

namespace Questionable.Model.Questing.Converter;

public sealed class LockedSkipConditionConverter() : EnumConverter<ELockedSkipCondition>(Values)
{
    private static readonly Dictionary<ELockedSkipCondition, string> Values = new()
    {
        { ELockedSkipCondition.Locked, "Locked" },
        { ELockedSkipCondition.Unlocked, "Unlocked" },
    };
}
