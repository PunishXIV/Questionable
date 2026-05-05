using System.Collections.Generic;
using Questionable.Model.Common.Converter;
namespace Questionable.Model.Questing.Converter;

public sealed class StatusConverter() : EnumConverter<EStatus>(Values)
{
    private static readonly Dictionary<EStatus, string> Values = new()
    {
        { EStatus.Triangulate, "Triangulate" },
        { EStatus.GatheringRateUp, "GatheringRateUp" },
        { EStatus.Prospect, "Prospect" },
        { EStatus.Eukrasia, "Eukrasia" },
        { EStatus.Jog, "Jog" },
        { EStatus.Transparent, "Transparent" },
        { EStatus.Hidden, "Hidden" },
        { EStatus.Transfiguration, "Transfiguration" }
    };
}
