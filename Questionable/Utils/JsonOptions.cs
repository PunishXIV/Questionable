using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Questionable.Utils;

internal static class JsonOptions
{
    internal static readonly JsonSerializerOptions Default = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { NoEmptyCollectionModifier, AlwaysSerializeModifier, DefaultTrueModifier, SkipDefaultObjectModifier }
        }
    };
    internal static void NoEmptyCollectionModifier(JsonTypeInfo typeInfo)
    {
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (typeof(ICollection).IsAssignableFrom(property.PropertyType))
                property.ShouldSerialize = (_, value) => value is ICollection { Count: > 0 };
        }
    }
    internal static void AlwaysSerializeModifier(JsonTypeInfo typeInfo)
    {
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (property.AttributeProvider?
                    .GetCustomAttributes(typeof(AlwaysSerializeAttribute), inherit: false)
                    .Length > 0)
            {
                property.ShouldSerialize = (_, _) => true;
            }
        }
    }
    internal static void DefaultTrueModifier(JsonTypeInfo typeInfo)
    {
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (property.AttributeProvider?
                    .GetCustomAttributes(typeof(DefaultTrueAttribute), inherit: false)
                    .Length > 0)
            {
                property.ShouldSerialize = (_, value) => value is false;
            }
        }
    }
    internal static void SkipDefaultObjectModifier(JsonTypeInfo typeInfo)
    {
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (property.AttributeProvider?
                    .GetCustomAttributes(typeof(IgnoreWhenDefaultInstanceAttribute), inherit: false)
                    .Length == 0)
                continue;

            object? defaultInstance = null;
            try { defaultInstance = Activator.CreateInstance(property.PropertyType); }
            catch { continue; }

            string defaultJson = JsonSerializer.Serialize(defaultInstance, Default);

            property.ShouldSerialize = (_, value) =>
                JsonSerializer.Serialize(value, Default) != defaultJson;
        }
    }
}
