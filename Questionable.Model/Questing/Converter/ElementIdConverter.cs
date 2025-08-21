using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing.Converter;

public sealed class ElementIdConverter : JsonConverter<ElementId>
{
    public override ElementId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new QuestId(reader.GetUInt16());
        else
            return ElementId.FromString(reader.GetString() ?? throw new JsonException());
    }

    public override void Write(Utf8JsonWriter writer, ElementId value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
