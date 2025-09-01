using Questionable.Model.Common.Converter;
using System.Collections.Generic;

namespace Questionable.Model.Questing.Converter;

public sealed class StatusConverter() : EnumConverter<EStatus>(Values)
{
    private static readonly Dictionary<EStatus, string> Values = new()
    {
        { EStatus.Hidden, "Hidden" },
    };
}
