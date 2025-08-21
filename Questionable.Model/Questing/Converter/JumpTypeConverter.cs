using System.Collections.Generic;
using Questionable.Model.Common.Converter;

namespace Questionable.Model.Questing.Converter;

public sealed class JumpTypeConverter() : EnumConverter<EJumpType>(Values)
{
    private static readonly Dictionary<EJumpType, string> Values = new()
    {
        { EJumpType.SingleJump, "SingleJump" },
        { EJumpType.RepeatedJumps, "RepeatedJumps" },
    };
}
