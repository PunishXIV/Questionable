using System;
using Newtonsoft.Json;
using Questionable.Model.Questing;

namespace Questionable.Converters;

public sealed class ElementIdNConverter : JsonConverter<ElementId>
{
    public override void WriteJson(JsonWriter writer, ElementId? value, JsonSerializer serializer) => writer.WriteValue(value?.ToString());

    public override ElementId? ReadJson(JsonReader reader, Type objectType, ElementId? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        string? value = reader.Value?.ToString();
        return value != null ? ElementId.FromString(value) : null;
    }
}