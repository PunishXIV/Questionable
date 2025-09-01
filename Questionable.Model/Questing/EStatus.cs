using Questionable.Model.Questing.Converter;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing;

[JsonConverter(typeof(StatusConverter))]
public enum EStatus : uint
{
    Triangulate = 217,
    GatheringRateUp = 218,
    Prospect = 225,
    Hidden = 614,
    Eukrasia = 2606,
    Jog = 4209,
}
