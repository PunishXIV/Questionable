using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Questionable.Model.Common.Converter;

public sealed class VectorConverter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

        Vector3 vec = new Vector3();
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

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(nameof(Vector3.X), value.X);
        writer.WriteNumber(nameof(Vector3.Y), value.Y);
        writer.WriteNumber(nameof(Vector3.Z), value.Z);
        writer.WriteEndObject();
    }
}
