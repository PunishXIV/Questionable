using System.Text.Json.Serialization;
using Questionable.Model.Common.Converter;

namespace Questionable.Model.Common;

[JsonConverter(typeof(GearsetUpdateSourceConverter))]
public enum EGearsetUpdateSource
{
    Vanilla,
    Stylist
}

public sealed class GearsetUpdateSourceConverter() : EnumConverter<EGearsetUpdateSource>();
