using System.Collections.Generic;
using Questionable.Model.Common.Converter;

namespace Questionable.Model.Questing.Converter;

public sealed class SkipConditionConverter() : EnumConverter<EExtraSkipCondition>(Values)
{
    private static readonly Dictionary<EExtraSkipCondition, string> Values = new()
    {
        { EExtraSkipCondition.WakingSandsMainArea, "WakingSandsMainArea" },
        { EExtraSkipCondition.WakingSandsSolar, "WakingSandsSolar" },
        { EExtraSkipCondition.RisingStonesSolar, "RisingStonesSolar"},
        { EExtraSkipCondition.RoguesGuild, "RoguesGuild"},
        { EExtraSkipCondition.NotRoguesGuild, "NotRoguesGuild"},
        { EExtraSkipCondition.DockStorehouse, "DockStorehouse"},
    };
}
