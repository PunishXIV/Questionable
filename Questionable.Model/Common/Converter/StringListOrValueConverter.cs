using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Questionable.Model.Common.Converter;

public sealed class StringListOrValueConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return [reader.GetString()!];

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException();
        reader.Read();

        List<string> value = [];
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            value.Add(reader.GetString()!);
            reader.Read();
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else if (value.Count == 1)
            writer.WriteStringValue(value[0]);
        else
        {
            writer.WriteStartArray();
            foreach (var v in value)
                writer.WriteStringValue(v);
            writer.WriteEndArray();
        }
    }
}
