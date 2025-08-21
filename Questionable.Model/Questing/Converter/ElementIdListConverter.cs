using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing.Converter;

public sealed class ElementIdListConverter : JsonConverter<List<ElementId>>
{
    public override List<ElementId> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException();

        reader.Read();

        List<ElementId> values = [];
        while (reader.TokenType != JsonTokenType.EndArray)
        {

            if (reader.TokenType == JsonTokenType.Number)
                values.Add(new QuestId(reader.GetUInt16()));
            else
                values.Add(ElementId.FromString(reader.GetString() ?? throw new JsonException()));

            reader.Read();
        }

        return values;
    }

    public override void Write(Utf8JsonWriter writer, List<ElementId> value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
