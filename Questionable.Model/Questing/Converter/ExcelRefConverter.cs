using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing.Converter;

public sealed class ExcelRefConverter : JsonConverter<ExcelRef>
{
    public override ExcelRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => ExcelRef.FromKey(reader.GetString()!),
            JsonTokenType.Number => ExcelRef.FromRowId(reader.GetUInt32()),
            _ => null
        };
    }

    public override void Write(Utf8JsonWriter writer, ExcelRef? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else if (value.Type == ExcelRef.EType.Key)
            writer.WriteStringValue(value.AsKey());
        else if (value.Type == ExcelRef.EType.RowId)
            writer.WriteNumberValue(value.AsRowId());
        else
            throw new JsonException();
    }
}
