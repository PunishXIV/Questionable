using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Questionable.Model.Common.Converter;

public sealed class VectorConverter : JsonConverter<Vector3>
{
    // Reads [x, y, z] and, for paths written before quest-v2, {"X": x, "Y": y, "Z": z}.
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            Vector3 array = new(ReadNumber(ref reader), ReadNumber(ref reader), ReadNumber(ref reader));
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                throw new JsonException();
            return array;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

        Vector3 vec = new();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    string? propertyName = reader.GetString();
                    if (propertyName == null || !reader.Read())
                        throw new JsonException();

                    switch (propertyName)
                    {
                        case nameof(Vector3.X):
                            vec.X = reader.GetSingle();
                            break;

                        case nameof(Vector3.Y):
                            vec.Y = reader.GetSingle();
                            break;

                        case nameof(Vector3.Z):
                            vec.Z = reader.GetSingle();
                            break;

                        default:
                            throw new JsonException();
                    }

                    break;

                case JsonTokenType.EndObject:
                    return vec;

                default:
                    throw new JsonException();
            }
        }

        throw new JsonException();
    }

    private static float ReadNumber(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.Number)
            throw new JsonException();
        return reader.GetSingle();
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }
}
