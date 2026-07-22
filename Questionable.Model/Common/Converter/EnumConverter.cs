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

    protected EnumConverter(IReadOnlyDictionary<T, string>? values = null, bool? shard = null)
    {
        if (values == null)
        {
            var names = Enum.GetValues(typeof(T)).Cast<T>().ToDictionary(x => x, x => x.ToString());
            _enumToString = new ReadOnlyDictionary<T, string>(names);
        }
        else
        {
            var missing = Enum.GetValues(typeof(T)).Cast<T>().Where(v => !values.ContainsKey(v) && v.ToString() != "None").ToList();
            if (shard != null && typeof(T) == typeof(EAetheryteLocation))
            {
                // Aetheryte locations are split across two converters: one for large aetherytes
                // and one for aethernet shards. Each converter should only demand coverage for
                // members that belong to its subset.
                bool wantShard = shard.Value;
                missing = missing
                    .Where(v => ((EAetheryteLocation)(object)v).IsAethernetShard() == wantShard)
                    .ToList();
                if (missing.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"{GetType().Name}: dictionary is missing entries for {typeof(T).Name} member(s): "
                        + string.Join(", ", missing)
                        + ". Add them to the converter's Values dictionary.");
                }
            }

            var tmpDict = values is IDictionary<T, string> dict
                ? new(dict)
                : new Dictionary<T, string>(values.ToDictionary(x => x.Key, x => x.Value));
            foreach (var item in missing)
            {
                tmpDict[item] = item.ToString();
            }
            _enumToString = new(tmpDict);
        }
        _stringToEnum = new(_enumToString.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal));
    }

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException();

        string? str = reader.GetString() ?? throw new JsonException();
        return _stringToEnum.TryGetValue(str, out T? value) ? value : throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(_enumToString[value]);
}
