using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Questionable.Model.Common.Converter;

public abstract class EnumConverter<T> : JsonConverter<T>
    where T : Enum
{
    private readonly ReadOnlyDictionary<T, string> _enumToString;
    private readonly ReadOnlyDictionary<string, T> _stringToEnum;

    protected EnumConverter(IReadOnlyDictionary<T, string> values)
    {
        _enumToString = values is IDictionary<T, string> dict
            ? new ReadOnlyDictionary<T, string>(dict)
            : new ReadOnlyDictionary<T, string>(values.ToDictionary(x => x.Key, x => x.Value));
        _stringToEnum = new ReadOnlyDictionary<string, T>(_enumToString.ToDictionary(x => x.Value, x => x.Key));
    }

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException();

        string? str = reader.GetString();
        if (str == null)
            throw new JsonException();

        return _stringToEnum.TryGetValue(str, out T? value) ? value : throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(_enumToString[value]);
    }
}
