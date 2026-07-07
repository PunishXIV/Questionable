using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing.Converter;

public sealed class HookTypeFilterConverter : JsonConverter<HookTypeFilter>
{
    public override HookTypeFilter Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True)
            return HookTypeFilter.AllHooksets;

        if (reader.TokenType == JsonTokenType.StartObject)
            return HookTypeFilter.From(JsonSerializer.Deserialize<Hookset>(ref reader, options)!);

        throw new JsonException("Hook type filter must be true or a hookset object.");
    }

    public override void Write(Utf8JsonWriter writer, HookTypeFilter value, JsonSerializerOptions options)
    {
        if (value.IsAllHooksets)
            writer.WriteBooleanValue(true);
        else
            JsonSerializer.Serialize(writer, value.Hookset, options);
    }
}
