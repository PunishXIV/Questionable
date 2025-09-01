using Questionable.Model.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing.Converter;

public sealed class AethernetShortcutConverter : JsonConverter<AethernetShortcut>
{
    private static readonly Dictionary<EAetheryteLocation, string> EnumToString = AethernetShardConverter.Values;
    private static readonly Dictionary<string, EAetheryteLocation> StringToEnum =
        EnumToString.ToDictionary(x => x.Value, x => x.Key);

    public override AethernetShortcut Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException();

        if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            throw new JsonException();

        string from = reader.GetString() ?? throw new JsonException();

        if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            throw new JsonException();

        string to = reader.GetString() ?? throw new JsonException();

        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException();

        return new AethernetShortcut
        {
            From = StringToEnum.TryGetValue(from, out var fromEnum) ? fromEnum : throw new JsonException(),
            To = StringToEnum.TryGetValue(to, out var toEnum) ? toEnum : throw new JsonException()
        };
    }

    public override void Write(Utf8JsonWriter writer, AethernetShortcut value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteStringValue(EnumToString[value.From]);
        writer.WriteStringValue(EnumToString[value.To]);
        writer.WriteEndArray();
    }
}
