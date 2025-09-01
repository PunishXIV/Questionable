using Questionable.Model.Common.Converter;
using System.Collections.Generic;

namespace Questionable.Model.Questing.Converter;

public sealed class QuestWorkModeConverter() : EnumConverter<EQuestWorkMode>(Values)
{
    private static readonly Dictionary<EQuestWorkMode, string> Values = new()
    {
        { EQuestWorkMode.Bitwise, "Bitwise" },
        { EQuestWorkMode.Exact, "Exact" },
    };
}
